using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransitGames.Core.Games;
using TransitGames.Core.Sessions;
using TransitGames.UI.Games;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;

namespace TransitGames.App.ViewModels;

/// <summary>New game wizard: role selection (area/stations/game settings steps are added by later milestones).</summary>
public sealed partial class NewGameViewModel : ObservableObject, IQueryAttributable
{
    public const string GameIdKey = "gameId";

    private readonly GameRegistry _registry;
    private readonly GameUiRegistry _uiRegistry;
    private readonly GameSessionManager _sessions;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private IGameModule? _module;

    public NewGameViewModel(
        GameRegistry registry,
        GameUiRegistry uiRegistry,
        GameSessionManager sessions,
        INavigationService navigation,
        IDialogService dialogs)
    {
        _registry = registry;
        _uiRegistry = uiRegistry;
        _sessions = sessions;
        _navigation = navigation;
        _dialogs = dialogs;
    }

    public ObservableCollection<GameRole> Roles { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = UiStrings.NewGameTitle;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial GameRole? SelectedRole { get; set; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(GameIdKey, out var value) && value is string gameId)
        {
            _module = _registry.Get(gameId);
            Title = $"{_module.Icon} {_module.DisplayName}";
            Description = _module.Description;
            Roles.Clear();
            foreach (var role in _module.Roles)
            {
                Roles.Add(role);
            }

            SelectedRole = null;
        }
    }

    private bool CanStart() => SelectedRole is not null;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (_module is null || SelectedRole is null)
        {
            await _dialogs.AlertAsync(UiStrings.Error, UiStrings.SelectRoleFirst);
            return;
        }

        var session = await _sessions.StartNewAsync(_module.Id, _module.CreateDefaultSettings(), SelectedRole.Id);

        // "../" replaces the wizard with the game screen, so Back from the game returns to the start page.
        await _navigation.GoToAsync("../" + _uiRegistry.Get(session.GameId).GetGameRoute(session.LocalRoleId));
    }
}
