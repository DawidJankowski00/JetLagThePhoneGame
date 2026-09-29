using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransitGames.Core.Events;
using TransitGames.Core.Geography;
using TransitGames.Tracking.Playback;
using TransitGames.Tracking.Storage;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;

namespace TransitGames.App.ViewModels;

/// <summary>Recorded track on the map with a time slider and playback of the position.</summary>
public sealed partial class TrackPreviewViewModel : ObservableObject, IQueryAttributable
{
    public const string TrackIdKey = "trackId";

    /// <summary>Playback shows the whole track in this time, whatever its real duration.</summary>
    private static readonly TimeSpan PlaybackLength = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);

    private readonly ITrackStore _store;
    private readonly IGameEventLog _events;
    private TrackPlayback? _playback;
    private IDispatcherTimer? _timer;

    public TrackPreviewViewModel(ITrackStore store, IGameEventLog events)
    {
        _store = store;
        _events = events;
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
        Subtitle = $"{track.StartedAt.ToLocalTime():g} · {DisplayFormat.Distance(track.DistanceMeters)}";
        Lines = _playback.Segments;
        Waypoints = track.SessionId is { } sessionId
            ? (await _events.GetEventsAsync(sessionId)).Where(e => e.Location is not null).Select(e => e.Location!.Value).ToList()
            : [];
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
