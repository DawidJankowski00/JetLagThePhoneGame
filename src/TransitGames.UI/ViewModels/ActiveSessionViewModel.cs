using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransitGames.Core.Games;
using TransitGames.Core.Geography;
using TransitGames.Core.Location;
using TransitGames.Core.Sessions;
using TransitGames.Core.Stations;
using TransitGames.Core.Sync;
using TransitGames.Core.Timing;
using TransitGames.Geo.Areas;
using TransitGames.Tracking;
using TransitGames.UI.Maps;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;

namespace TransitGames.UI.ViewModels;

/// <summary>
/// Shared by all in-game screens: header (game name, role, elapsed time, recording indicator, online status, game
/// code, what the own position reveals, "finish game") and the map basics (game area and stations copied into the
/// session settings, own position, other players the local role may see).
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
    private readonly OnlineSessionCoordinator _online;
    private readonly IShare _share;
    private IDispatcherTimer? _timer;
    private int _ticks;
    private Guid? _mapSessionId;

    public ActiveSessionViewModel(
        GameSessionManager sessions,
        GameRegistry registry,
        IClock clock,
        IDialogService dialogs,
        INavigationService navigation,
        ILocationTracker location,
        TrackRecorder recorder,
        TrackingCoordinator tracking,
        OnlineSessionCoordinator online,
        IShare share)
    {
        _sessions = sessions;
        _registry = registry;
        _clock = clock;
        _dialogs = dialogs;
        _navigation = navigation;
        _location = location;
        _recorder = recorder;
        _tracking = tracking;
        _online = online;
        _share = share;
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

    [ObservableProperty]
    public partial bool IsOnlineGame { get; set; }

    /// <summary>"● Online · 2/2 players", "⚠ No connection – messages are queued"...</summary>
    [ObservableProperty]
    public partial string ConnectionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CodeText { get; set; } = string.Empty;

    /// <summary>Who sees this device's position and how often (online games).</summary>
    [ObservableProperty]
    public partial string SharingText { get; set; } = string.Empty;

    /// <summary>Other players the local role may see (online games).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<MapMarker> Players { get; set; } = [];

    public IGameSession? Session => _sessions.Current;

    /// <summary>Call from the page's OnAppearing.</summary>
    public void Activate()
    {
        if (_sessions.Current is { } session && _registry.TryGet(session.GameId, out var module) && module is not null)
        {
            GameName = module.DisplayName;
            RefreshRole();
            if (session.Id != _mapSessionId)
            {
                _mapSessionId = session.Id;
                Area = session.Settings.Area is { } area ? GameArea.FromDefinition(area) : null;
                Stations = session.Settings.Stations;
            }

            _tracking.OnGameScreenShown(session);
            _online.OnGameScreenShown(session);
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
        _online.Changed -= OnOnlineChanged;
        _online.Changed += OnOnlineChanged;
        OwnLocation = _location.LastLocation?.Point ?? OwnLocation;
        IsRecording = _recorder.IsRecording;
        RefreshOnline();
    }

    /// <summary>Updates the role shown in the header (roles can change during a game, e.g. after a catch in Tag).</summary>
    public void RefreshRole()
    {
        if (_sessions.Current is { } session && _registry.TryGet(session.GameId, out var module) && module is not null)
        {
            RoleName = module.Roles.FirstOrDefault(r => r.Id == session.LocalRoleId)?.DisplayName ?? session.LocalRoleId;
        }
    }

    /// <summary>Call from the page's OnDisappearing.</summary>
    public void Deactivate()
    {
        _timer?.Stop();
        _location.LocationChanged -= OnLocationChanged;
        _recorder.StateChanged -= OnRecorderStateChanged;
        _online.Changed -= OnOnlineChanged;
    }

    private void OnLocationChanged(object? sender, LocationSample sample) =>
        MainThread.BeginInvokeOnMainThread(() => OwnLocation = sample.Point);

    private void OnRecorderStateChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() => IsRecording = _recorder.IsRecording);

    private void OnOnlineChanged(object? sender, EventArgs e) => MainThread.BeginInvokeOnMainThread(RefreshOnline);

    private void RefreshOnline()
    {
        var session = _sessions.Current;
        var link = _online.Link;
        IsOnlineGame = link is not null && session is not null && _online.SessionId == session.Id;
        if (!IsOnlineGame || link is null)
        {
            ConnectionText = CodeText = SharingText = string.Empty;
            Players = [];
            return;
        }

        var peers = _online.Peers;
        var state = _online.IsRejected
            ? UiStrings.OnlineRejected
            : _online.State switch
            {
                SyncConnectionState.Connected => UiStrings.OnlineConnected,
                SyncConnectionState.Connecting => UiStrings.OnlineConnecting,
                _ => UiStrings.OnlineReconnecting,
            };
        ConnectionText = _online.State == SyncConnectionState.Connected && peers.Count > 0
            ? $"{state} · {string.Format(UiStrings.OnlinePlayersFormat, peers.Count(p => p.IsConnected), peers.Count)}"
            : state;
        CodeText = string.Format(UiStrings.OnlineCodeFormat, link.Code);
        SharingText = DescribeSharing(_online.Sharing);
        RefreshPlayers();
    }

    private void RefreshPlayers()
    {
        var now = _clock.UtcNow;
        Players = _online.Players
            .Select(p => new MapMarker(
                p.Point,
                p.IsTeammate ? MapPalette.Teammate : MapPalette.Opponent,
                $"{p.Name} · {Age(now - p.Timestamp)}"))
            .ToList();
    }

    private static string DescribeSharing(LocationShareInfo sharing)
    {
        if (!sharing.IsShared)
        {
            return UiStrings.OnlineSharingPrivate;
        }

        var interval = sharing.Interval <= TimeSpan.FromMinutes(1)
            ? UiStrings.OnlineIntervalLive
            : string.Format(UiStrings.OnlineIntervalMinutesFormat, Math.Round(sharing.Interval.TotalMinutes));
        if (sharing.LastSharedAt is { } last)
        {
            interval = string.Format(UiStrings.OnlineSharingLastFormat, interval, last.ToLocalTime());
        }

        return string.Format(UiStrings.OnlineSharingFormat, string.Join(", ", sharing.ViewerRoleNames), interval);
    }

    private static string Age(TimeSpan age) =>
        age < TimeSpan.FromMinutes(1)
            ? UiStrings.OnlinePlayerAgeNow
            : string.Format(UiStrings.OnlinePlayerAgeMinutesFormat, (int)age.TotalMinutes);

    [RelayCommand]
    private async Task ShareCodeAsync()
    {
        if (_online.Link is { } link)
        {
            await _share.RequestAsync(new ShareTextRequest
            {
                Title = UiStrings.OnlineShareCode,
                Text = string.Format(UiStrings.OnlineShareCodeText, GameName, link.Code),
            });
        }
    }

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

        // Player labels show the age of the position ("3 min ago").
        if (++_ticks % 15 == 0 && IsOnlineGame)
        {
            RefreshPlayers();
        }
    }
}
