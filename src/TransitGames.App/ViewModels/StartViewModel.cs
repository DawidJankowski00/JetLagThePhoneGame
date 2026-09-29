using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransitGames.Core.Games;
using TransitGames.Core.Sessions;
using TransitGames.UI.Games;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;

namespace TransitGames.App.ViewModels;

public sealed record GameCard(IGameModule Module)
{
    public string Icon => Module.Icon;
    public string DisplayName => Module.DisplayName;
    public string Description => Module.Description;
    public string MinPlayersText => string.Format(UiStrings.MinPlayersFormat, Module.MinPlayers);
}

public sealed partial class StartViewModel : ObservableObject
{
    private static bool s_autoResumeDone;

    private readonly GameRegistry _registry;
    private readonly GameUiRegistry _uiRegistry;
    private readonly GameSessionManager _sessions;
    private readonly INavigationService _navigation;

    public StartViewModel(
        GameRegistry registry,
        GameUiRegistry uiRegistry,
        GameSessionManager sessions,
        INavigationService navigation)
    {
        _registry = registry;
        _uiRegistry = uiRegistry;
        _sessions = sessions;
        _navigation = navigation;
        Games = new ObservableCollection<GameCard>(registry.Modules.Select(m => new GameCard(m)));
    }

    public ObservableCollection<GameCard> Games { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResumableSession))]
    public partial string? ResumeText { get; set; }

    public bool HasResumableSession => ResumeText is not null;

    public async Task OnAppearingAsync()
    {
        var session = await _sessions.TryResumeAsync();
        ResumeText = session is not null && _registry.TryGet(session.GameId, out var module) && module is not null
            ? string.Format(UiStrings.ResumeGameFormat, module.DisplayName)
            : null;

        // After the OS killed the app, go straight back into the running game.
        if (session is not null && !s_autoResumeDone)
        {
            s_autoResumeDone = true;
            await ResumeAsync();
        }

        s_autoResumeDone = true;
    }

    [RelayCommand]
    private Task JoinOnlineAsync() => _navigation.GoToAsync(Routes.JoinGame);

    [RelayCommand]
    private Task SelectGameAsync(GameCard card) =>
        _navigation.GoToAsync(Routes.NewGame, new Dictionary<string, object> { [NewGameViewModel.GameIdKey] = card.Module.Id });

    [RelayCommand]
    private async Task ResumeAsync()
    {
        if (_sessions.Current is { } session)
        {
            await _navigation.GoToAsync(_uiRegistry.Get(session.GameId).GetGameRoute(session.LocalRoleId));
        }
    }
}
