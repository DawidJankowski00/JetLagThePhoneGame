using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransitGames.Core.Games;
using TransitGames.Core.Geography;
using TransitGames.Core.Location;
using TransitGames.Core.Sessions;
using TransitGames.Core.Stations;
using TransitGames.Core.Timing;
using TransitGames.Geo.Areas;
using TransitGames.Tracking;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;

namespace TransitGames.UI.ViewModels;

/// <summary>
/// Shared by all in-game screens: header (game name, role, elapsed time, recording indicator, "finish game") and
/// the map basics (game area and stations copied into the session settings, own position).
/// </summary>
public sealed partial class ActiveSessionViewModel : ObservableObject
{
    private readonly GameSessionManager _sessions;
    private readonly GameRegistry _registry;
    private readonly IClock _clock;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly ILocationTracker _location;
    private readonly TrackRecorder _recorder;
    private readonly TrackingCoordinator _tracking;
    private IDispatcherTimer? _timer;
    private Guid? _mapSessionId;

    public ActiveSessionViewModel(
        GameSessionManager sessions,
        GameRegistry registry,
        IClock clock,
        IDialogService dialogs,
        INavigationService navigation,
        ILocationTracker location,
        TrackRecorder recorder,
        TrackingCoordinator tracking)
    {
        _sessions = sessions;
        _registry = registry;
        _clock = clock;
        _dialogs = dialogs;
        _navigation = navigation;
        _location = location;
        _recorder = recorder;
        _tracking = tracking;
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

    /// <summary>This device's last GPS fix (never shared by itself, see LocationSharingPolicy).</summary>
    [ObservableProperty]
    public partial GeoPoint? OwnLocation { get; set; }

    [ObservableProperty]
    public partial bool IsRecording { get; set; }

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

            _tracking.OnGameScreenShown(session);
        }

        _timer ??= Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
        OnTick(null, EventArgs.Empty);

        _location.LocationChanged -= OnLocationChanged;
        _location.LocationChanged += OnLocationChanged;
        _recorder.StateChanged -= OnRecorderStateChanged;
        _recorder.StateChanged += OnRecorderStateChanged;
        OwnLocation = _location.LastLocation?.Point ?? OwnLocation;
        IsRecording = _recorder.IsRecording;
    }

    /// <summary>Call from the page's OnDisappearing.</summary>
    public void Deactivate()
    {
        _timer?.Stop();
        _location.LocationChanged -= OnLocationChanged;
        _recorder.StateChanged -= OnRecorderStateChanged;
    }

    private void OnLocationChanged(object? sender, LocationSample sample) =>
        MainThread.BeginInvokeOnMainThread(() => OwnLocation = sample.Point);

    private void OnRecorderStateChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() => IsRecording = _recorder.IsRecording);

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
