using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TransitGames.App.Services;
using TransitGames.Core.Sessions;
using TransitGames.Sync;
using TransitGames.UI.Games;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;
using TransitGames.UI.Settings;

namespace TransitGames.App.ViewModels;

/// <summary>Last wizard step: the game's own settings (view supplied by the game), online or not, start.</summary>
public sealed partial class GameSetupViewModel : ObservableObject
{
    private readonly NewGameWizard _wizard;
    private readonly GameSessionManager _sessions;
    private readonly GameUiRegistry _uiRegistry;
    private readonly OnlinePreferences _onlinePreferences;
    private readonly OnlineGameService _onlineGames;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IServiceProvider _services;
    private readonly ILogger<GameSetupViewModel> _logger;

    public GameSetupViewModel(
        NewGameWizard wizard,
        GameSessionManager sessions,
        GameUiRegistry uiRegistry,
        OnlinePreferences onlinePreferences,
        OnlineGameService onlineGames,
        IDialogService dialogs,
        INavigationService navigation,
        IServiceProvider services,
        ILogger<GameSetupViewModel> logger)
    {
        _wizard = wizard;
        _sessions = sessions;
        _uiRegistry = uiRegistry;
        _onlinePreferences = onlinePreferences;
        _onlineGames = onlineGames;
        _dialogs = dialogs;
        _navigation = navigation;
        _services = services;
        _logger = logger;
        PlayOnline = onlinePreferences.IsConfigured;
    }

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    /// <summary>Create the game on the server so the other team can join with a code.</summary>
    [ObservableProperty]
    public partial bool PlayOnline { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string? BusyText { get; set; }

    public bool IsBusy => BusyText is not null;

    /// <summary>The game's settings editor (created once per wizard run), or null. The area and stations are already set.</summary>
    public View? CreateSettingsView()
    {
        var module = _wizard.RequireModule();
        Summary = string.Format(UiStrings.GameSetupSummaryFormat, module.DisplayName, _wizard.Role?.DisplayName, _wizard.Stations.Count);
        var settings = _wizard.RequireSettings();
        settings.Area = _wizard.RequireArea().ToDefinition();
        settings.Stations = [.. _wizard.Stations];
        return _uiRegistry.Get(module.Id).CreateSettingsView(settings, _services);
    }

    private bool CanStart() => BusyText is null;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var module = _wizard.RequireModule();
        var area = _wizard.RequireArea();
        if (_wizard.Role is not { } role || _wizard.Stations.Count == 0)
        {
            await _dialogs.AlertAsync(UiStrings.Error, UiStrings.StationsNoneEnabled);
            return;
        }

        var settings = _wizard.RequireSettings();
        settings.Area = area.ToDefinition();
        settings.Stations = [.. _wizard.Stations];

        string? code = null;
        IGameSession session;
        if (PlayOnline)
        {
            if (!_onlinePreferences.IsConfigured)
            {
                await _dialogs.AlertAsync(UiStrings.OnlineErrorTitle, UiStrings.OnlineServerMissing);
                return;
            }

            try
            {
                BusyText = UiStrings.OnlineCreating;
                (session, var ticket) = await _onlineGames.StartHostedAsync(module, settings, role.Id);
                code = ticket.Code;
            }
            catch (OnlineException ex)
            {
                _logger.LogWarning(ex, "Creating the online game failed");
                await _dialogs.ShowAsync(ex);
                return;
            }
            finally
            {
                BusyText = null;
            }
        }
        else
        {
            session = await _sessions.StartNewAsync(module.Id, settings, role.Id);
        }

        // Absolute route: the wizard pages are dropped and Back from the game returns to the start page.
        await _navigation.GoToAsync($"//{Routes.Start}/{_uiRegistry.Get(session.GameId).GetGameRoute(session.LocalRoleId)}");
        if (code is not null)
        {
            await _dialogs.AlertAsync(UiStrings.OnlineGameCreatedTitle, string.Format(UiStrings.OnlineGameCreatedFormat, code));
        }
    }
}
