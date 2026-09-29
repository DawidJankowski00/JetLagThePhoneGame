using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransitGames.Core.Events;
using TransitGames.Core.Games;
using TransitGames.Core.Geography;
using TransitGames.Core.Sessions;
using TransitGames.Geo.Areas;
using TransitGames.Tracking.Playback;
using TransitGames.Tracking.Storage;
using TransitGames.UI.Games;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;

namespace TransitGames.App.ViewModels;

/// <summary>
/// Recorded track on the map with a time slider and playback of the position. A game's track also shows the game
/// area and the game's events: the latest one at the slider time, with jumps to the previous and next event.
/// </summary>
public sealed partial class TrackPreviewViewModel : ObservableObject, IQueryAttributable
{
    public const string TrackIdKey = "trackId";

    /// <summary>Playback shows the whole track in this time, whatever its real duration.</summary>
    private static readonly TimeSpan PlaybackLength = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);

    private readonly ITrackStore _store;
    private readonly IGameEventLog _events;
    private readonly IGameSessionStore _sessions;
    private readonly GameRegistry _registry;
    private readonly GameUiRegistry _uiRegistry;
    private TrackPlayback? _playback;
    private IDispatcherTimer? _timer;
    private IReadOnlyList<GameEvent> _gameEvents = [];
    private IGameUiModule? _game;

    public TrackPreviewViewModel(
        ITrackStore store,
        IGameEventLog events,
        IGameSessionStore sessions,
        GameRegistry registry,
        GameUiRegistry uiRegistry)
    {
        _store = store;
        _events = events;
        _sessions = sessions;
        _registry = registry;
        _uiRegistry = uiRegistry;
    }

    [ObservableProperty]
    public partial string Title { get; set; } = UiStrings.TrackPreviewTitle;

    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<IReadOnlyList<GeoPoint>> Lines { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<GeoPoint> Waypoints { get; set; } = [];

    [ObservableProperty]
    public partial GeoPoint? Position { get; set; }

    [ObservableProperty]
    public partial GameArea? Area { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEvents))]
    public partial int EventCount { get; set; }

    public bool HasEvents => EventCount > 0;

    /// <summary>The latest game event at the slider time.</summary>
    [ObservableProperty]
    public partial string EventText { get; set; } = string.Empty;

    /// <summary>Slider position, 0..1.</summary>
    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string TimeText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayText))]
    public partial bool IsPlaying { get; set; }

    public string PlayText => IsPlaying ? "⏸" : "▶";

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(TrackIdKey, out var value) && value is Guid trackId)
        {
            await LoadAsync(trackId);
        }
    }

    public void OnDisappearing() => Pause();

    private async Task LoadAsync(Guid trackId)
    {
        var track = await _store.GetAsync(trackId);
        var points = await _store.GetPointsAsync(trackId);
        if (track is null || points.Count == 0)
        {
            return;
        }

        _playback = new TrackPlayback(points);
        Title = track.Name;
        var hours = _playback.Duration.TotalHours;
        Subtitle = $"{track.StartedAt.ToLocalTime():g} · " + string.Format(
            UiStrings.TrackStatsFormat,
            DisplayFormat.Distance(track.DistanceMeters),
            DisplayFormat.Duration(_playback.Duration),
            hours > 0 ? track.DistanceMeters / 1000 / hours : 0);
        Lines = _playback.Segments;
        if (track.SessionId is { } sessionId)
        {
            await LoadGameAsync(sessionId);
        }

        Waypoints = _gameEvents.Where(e => e.Location is not null).Select(e => e.Location!.Value).ToList();
        EventCount = _gameEvents.Count;
        Progress = 0;
        OnProgressChanged(0);
    }

    partial void OnProgressChanged(double value)
    {
        if (_playback is null)
        {
            return;
        }

        var time = _playback.TimeAtFraction(value);
        Position = _playback.PositionAt(time);
        TimeText = $"{time.ToLocalTime():HH:mm:ss}  (+{DisplayFormat.Duration(time - _playback.Start)})";
        var latest = _gameEvents.LastOrDefault(e => e.Timestamp <= time);
        EventText = latest is null
            ? UiStrings.TrackNoEventYet
            : $"{latest.Timestamp.ToLocalTime():HH:mm}  {_game?.EventIcon(latest.Type) ?? EventIcons.Default}  {latest.Title}";
    }

    [RelayCommand]
    private void PreviousEvent()
    {
        if (_playback is null)
        {
            return;
        }

        // Slightly before the current time, so pressing again moves on to the event before.
        var now = _playback.TimeAtFraction(Progress) - TimeSpan.FromSeconds(1);
        Jump(_gameEvents.LastOrDefault(e => e.Timestamp < now) ?? _gameEvents.FirstOrDefault());
    }

    [RelayCommand]
    private void NextEvent()
    {
        if (_playback is null)
        {
            return;
        }

        var now = _playback.TimeAtFraction(Progress) + TimeSpan.FromSeconds(1);
        Jump(_gameEvents.FirstOrDefault(e => e.Timestamp > now) ?? _gameEvents.LastOrDefault());
    }

    private void Jump(GameEvent? gameEvent)
    {
        if (gameEvent is not null && _playback is not null)
        {
            Pause();
            Progress = _playback.FractionAt(gameEvent.Timestamp);
        }
    }

    /// <summary>The game's area and events (the game may have been deleted from the history).</summary>
    private async Task LoadGameAsync(Guid sessionId)
    {
        _gameEvents = (await _events.GetEventsAsync(sessionId)).OrderBy(e => e.Timestamp).ToList();
        var snapshot = await _sessions.LoadAsync(sessionId);
        if (snapshot is null || !_registry.TryGet(snapshot.GameId, out var module) || module is null)
        {
            return;
        }

        _game = _uiRegistry.Get(snapshot.GameId);
        Area = module.DeserializeSettings(snapshot.SettingsJson).Area is { } area ? GameArea.FromDefinition(area) : null;
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (IsPlaying)
        {
            Pause();
            return;
        }

        if (_playback is null)
        {
            return;
        }

        if (Progress >= 1)
        {
            Progress = 0;
        }

        _timer ??= Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = FrameInterval;
        _timer.Tick -= OnFrame;
        _timer.Tick += OnFrame;
        _timer.Start();
        IsPlaying = true;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var next = Progress + FrameInterval / PlaybackLength;
        Progress = Math.Min(1, next);
        if (next >= 1)
        {
            Pause();
        }
    }

    private void Pause()
    {
        _timer?.Stop();
        IsPlaying = false;
    }
}
