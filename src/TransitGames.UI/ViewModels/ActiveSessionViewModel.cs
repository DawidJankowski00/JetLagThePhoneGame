using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransitGames.Core.Games;
using TransitGames.Core.Sessions;
using TransitGames.Core.Stations;
using TransitGames.Core.Timing;
using TransitGames.Geo.Areas;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;

namespace TransitGames.UI.ViewModels;

/// <summary>
/// Shared by all in-game screens: header (game name, role, elapsed time, "finish game") and the map basics
/// (game area and stations copied into the session settings).
/// </summary>
public sealed partial class ActiveSessionViewModel : ObservableObject
{
    private readonly GameSessionManager _sessions;
    private readonly GameRegistry _registry;
    private readonly IClock _clock;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private IDispatcherTimer? _timer;
    private Guid? _mapSessionId;

    public ActiveSessionViewModel(
        GameSessionManager sessions,
        GameRegistry registry,
        IClock clock,
        IDialogService dialogs,
        INavigationService navigation)
    {
        _sessions = sessions;
        _registry = registry;
        _clock = clock;
        _dialogs = dialogs;
        _navigation = navigation;
    }

    [ObservableProperty]
    public partial string GameName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RoleName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ElapsedText { get; set; } = "00:00:00";

    /// <summary>Parsed game area of the current session (null for sessions created without one).</summary>
    [ObservableProperty]
    public partial GameArea? Area { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<Station> Stations { get; set; } = [];

    public IGameSession? Session => _sessions.Current;

    /// <summary>Call from the page's OnAppearing.</summary>
    public void Activate()
    {
        if (_sessions.Current is { } session && _registry.TryGet(session.GameId, out var module) && module is not null)
        {
            GameName = module.DisplayName;
            RoleName = module.Roles.FirstOrDefault(r => r.Id == session.LocalRoleId)?.DisplayName ?? session.LocalRoleId;
            if (session.Id != _mapSessionId)
            {
                _mapSessionId = session.Id;
                Area = session.Settings.Area is { } area ? GameArea.FromDefinition(area) : null;
                Stations = session.Settings.Stations;
            }
        }

        _timer ??= Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
        OnTick(null, EventArgs.Empty);
    }

    /// <summary>Call from the page's OnDisappearing.</summary>
    public void Deactivate() => _timer?.Stop();

    [RelayCommand]
    private async Task FinishAsync()
    {
        if (!await _dialogs.ConfirmAsync(UiStrings.FinishGameTitle, UiStrings.FinishGameMessage))
        {
            return;
        }

        await _sessions.FinishAsync();
        await _navigation.GoHomeAsync();
    }

    [RelayCommand]
    private async Task LeaveAsync()
    {
        _sessions.Close();
        await _navigation.GoHomeAsync();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_sessions.Current is { } session)
        {
            var elapsed = _clock.UtcNow - session.CreatedAt;
            ElapsedText = elapsed < TimeSpan.Zero ? "00:00:00" : elapsed.ToString(@"hh\:mm\:ss");
        }
    }
}
