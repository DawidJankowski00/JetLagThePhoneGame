using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TransitGames.Core.Games;
using TransitGames.Core.Sync;
using TransitGames.Sync;
using TransitGames.UI.Games;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;
using TransitGames.UI.Settings;

namespace TransitGames.App.ViewModels;

public sealed record JoinRoleChoice(string RoleId, string Label, string Description);

/// <summary>Join an online game: enter the code, see the game and its players, pick the team's role.</summary>
public sealed partial class JoinGameViewModel : ObservableObject
{
    private readonly OnlineGameService _onlineGames;
    private readonly OnlinePreferences _preferences;
    private readonly GameRegistry _registry;
    private readonly GameUiRegistry _uiRegistry;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly ILogger<JoinGameViewModel> _logger;
    private LobbyInfo? _lobby;

    public JoinGameViewModel(
        OnlineGameService onlineGames,
        OnlinePreferences preferences,
        GameRegistry registry,
        GameUiRegistry uiRegistry,
        IDialogService dialogs,
        INavigationService navigation,
        ILogger<JoinGameViewModel> logger)
    {
        _onlineGames = onlineGames;
        _preferences = preferences;
        _registry = registry;
        _uiRegistry = uiRegistry;
        _dialogs = dialogs;
        _navigation = navigation;
        _logger = logger;
    }

    public ObservableCollection<JoinRoleChoice> Roles { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    public partial string Code { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ServerText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGame))]
    public partial string? GameTitle { get; set; }

    [ObservableProperty]
    public partial string PlayersText { get; set; } = string.Empty;

    public bool HasGame => GameTitle is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(FindCommand), nameof(JoinCommand))]
    public partial string? BusyText { get; set; }

    public bool IsBusy => BusyText is not null;

    public void OnAppearing() =>
        ServerText = _preferences.IsConfigured
            ? string.Format(UiStrings.JoinServerFormat, _preferences.ServerUrl)
            : UiStrings.OnlineServerMissing;

    partial void OnCodeChanged(string value)
    {
        if (_lobby is not null && OnlineProtocol.NormalizeCode(value) != _lobby.Code)
        {
            Clear();
        }
    }

    private bool CanFind() => BusyText is null && OnlineProtocol.NormalizeCode(Code).Length == OnlineProtocol.CodeLength;

    [RelayCommand(CanExecute = nameof(CanFind))]
    private async Task FindAsync()
    {
        if (!_preferences.IsConfigured)
        {
            await _dialogs.AlertAsync(UiStrings.OnlineErrorTitle, UiStrings.OnlineServerMissing);
            return;
        }

        try
        {
            BusyText = UiStrings.JoinSearching;
            var lobby = await _onlineGames.FindAsync(Code);
            if (lobby.IsFinished)
            {
                throw new OnlineException(OnlineErrorCodes.LobbyFinished, lobby.Code);
            }

            Show(lobby);
        }
        catch (OnlineException ex)
        {
            _logger.LogInformation(ex, "Looking up game {Code} failed", Code);
            Clear();
            await _dialogs.ShowAsync(ex);
        }
        finally
        {
            BusyText = null;
        }
    }

    private bool CanJoin() => BusyText is null;

    [RelayCommand(CanExecute = nameof(CanJoin))]
    private async Task JoinAsync(JoinRoleChoice choice)
    {
        if (_lobby is not { } lobby)
        {
            return;
        }

        try
        {
            BusyText = UiStrings.JoinJoining;
            var session = await _onlineGames.JoinAsync(lobby, choice.RoleId);

            // Absolute route: Back from the game returns to the start page.
            await _navigation.GoToAsync($"//{Routes.Start}/{_uiRegistry.Get(session.GameId).GetGameRoute(session.LocalRoleId)}");
        }
        catch (OnlineException ex)
        {
            _logger.LogWarning(ex, "Joining game {Code} failed", lobby.Code);
            await _dialogs.ShowAsync(ex);
        }
        finally
        {
            BusyText = null;
        }
    }

    private void Show(LobbyInfo lobby)
    {
        _lobby = lobby;

        // Prefer this app's localized names over the names sent by the host's app.
        var module = _registry.TryGet(lobby.GameId, out var m) ? m : null;
        GameTitle = $"{module?.Icon} {module?.DisplayName ?? lobby.GameName}".Trim();
        var roleNames = lobby.Roles.ToDictionary(
            r => r.Id,
            r => module?.Roles.FirstOrDefault(x => x.Id == r.Id)?.DisplayName ?? r.DisplayName);
        PlayersText = string.Format(
            UiStrings.JoinPlayersFormat,
            lobby.Players.Count == 0
                ? UiStrings.JoinNoPlayers
                : string.Join(", ", lobby.Players.Select(p => $"{p.Name} ({roleNames.GetValueOrDefault(p.RoleId, p.RoleId)})")));

        Roles.Clear();
        foreach (var role in lobby.Roles.Where(r => module?.InitialRoleId is null || r.Id == module.InitialRoleId))
        {
            var description = module?.Roles.FirstOrDefault(r => r.Id == role.Id)?.Description ?? string.Empty;
            Roles.Add(new JoinRoleChoice(role.Id, string.Format(UiStrings.JoinAsFormat, roleNames[role.Id]), description));
        }
    }

    private void Clear()
    {
        _lobby = null;
        GameTitle = null;
        PlayersText = string.Empty;
        Roles.Clear();
    }
}
