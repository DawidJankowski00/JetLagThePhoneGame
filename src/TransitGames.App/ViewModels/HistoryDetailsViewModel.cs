using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransitGames.Core.Events;
using TransitGames.Core.Games;
using TransitGames.Core.Sessions;
using TransitGames.Core.Timing;
using TransitGames.Tracking.Storage;
using TransitGames.UI.Games;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;

namespace TransitGames.App.ViewModels;

/// <param name="Offset">Time since the start of the game, e.g. "+0:35:10".</param>
public sealed record TimelineItem(string Time, string Offset, string Icon, string Title, string? Details)
{
    public bool HasDetails => !string.IsNullOrEmpty(Details);
}

/// <summary>One game from the history: result, timeline of events and the recorded track.</summary>
public sealed partial class HistoryDetailsViewModel : ObservableObject, IQueryAttributable
{
    public const string SessionIdKey = "sessionId";

    private readonly IGameSessionStore _store;
    private readonly GameRegistry _registry;
    private readonly GameUiRegistry _uiRegistry;
    private readonly GameSessionManager _sessions;
    private readonly IGameEventLog _events;
    private readonly ITrackStore _tracks;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IClock _clock;
    private Guid _sessionId;
    private Guid? _trackId;

    public HistoryDetailsViewModel(
        IGameSessionStore store,
        GameRegistry registry,
        GameUiRegistry uiRegistry,
        GameSessionManager sessions,
        IGameEventLog events,
        ITrackStore tracks,
        IDialogService dialogs,
        INavigationService navigation,
        IClock clock)
    {
        _store = store;
        _registry = registry;
        _uiRegistry = uiRegistry;
        _sessions = sessions;
        _events = events;
        _tracks = tracks;
        _dialogs = dialogs;
        _navigation = navigation;
        _clock = clock;
    }

    [ObservableProperty]
    public partial string Title { get; set; } = UiStrings.HistoryTitle;

    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    public partial string ResultHeadline { get; set; } = string.Empty;

    public bool HasResult => !string.IsNullOrEmpty(ResultHeadline);

    [ObservableProperty]
    public partial IReadOnlyList<GameResultLine> ResultLines { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTimelineEmpty))]
    public partial IReadOnlyList<TimelineItem> Timeline { get; set; } = [];

    public bool IsTimelineEmpty => Timeline.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrack))]
    public partial string? TrackText { get; set; }

    public bool HasTrack => TrackText is not null;

    [ObservableProperty]
    public partial bool CanResume { get; set; }

    [ObservableProperty]
    public partial bool CanFinish { get; set; }

    [ObservableProperty]
    public partial bool CanDelete { get; set; }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(SessionIdKey, out var value) && value is Guid sessionId)
        {
            _sessionId = sessionId;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        var snapshot = await _store.LoadAsync(_sessionId);
        if (snapshot is null || !_registry.TryGet(snapshot.GameId, out var module) || module is null)
        {
            return;
        }

        var session = module.RestoreSession(snapshot);
        var now = _clock.UtcNow;
        var end = session.FinishedAt ?? now;
        var role = module.Roles.FirstOrDefault(r => r.Id == session.LocalRoleId)?.DisplayName ?? session.LocalRoleId;
        Title = $"{module.Icon} {module.DisplayName}";
        Subtitle = string.Format(
            UiStrings.HistorySubtitleFormat,
            session.CreatedAt.ToLocalTime().ToString("g"),
            DisplayFormat.Duration(end - session.CreatedAt),
            role);
        StatusText = session.Status == SessionStatus.Finished ? UiStrings.StatusFinished : UiStrings.StatusRunning;

        var result = module.DescribeResult(session, now);
        ResultHeadline = result.Headline;
        ResultLines = result.Lines;

        var ui = _uiRegistry.Get(session.GameId);
        var events = await _events.GetEventsAsync(_sessionId);
        Timeline = events
            .OrderBy(e => e.Timestamp)
            .Select(e => new TimelineItem(
                e.Timestamp.ToLocalTime().ToString("HH:mm"),
                "+" + DisplayFormat.Duration(e.Timestamp - session.CreatedAt),
                ui.EventIcon(e.Type),
                e.Title,
                e.Details))
            .ToList();

        var track = (await _tracks.ListAsync()).FirstOrDefault(t => t.SessionId == _sessionId);
        _trackId = track?.Id;
        TrackText = track is null
            ? null
            : string.Format(UiStrings.HistoryTrackFormat, DisplayFormat.Distance(track.DistanceMeters), track.PointCount);

        var latestUnfinished = await _store.GetLatestUnfinishedAsync();
        CanResume = session.Status != SessionStatus.Finished && latestUnfinished?.SessionId == _sessionId;
        CanFinish = session.Status != SessionStatus.Finished && !CanResume;
        CanDelete = session.Status == SessionStatus.Finished;
    }

    [RelayCommand]
    private Task ShowTrackAsync() =>
        _trackId is { } id
            ? _navigation.GoToAsync(Routes.TrackPreview, new Dictionary<string, object> { [TrackPreviewViewModel.TrackIdKey] = id })
            : Task.CompletedTask;

    [RelayCommand]
    private async Task ResumeAsync()
    {
        var session = await _sessions.TryResumeAsync();
        if (session?.Id == _sessionId)
        {
            await _navigation.GoToAsync("//" + Routes.Start + "/" + _uiRegistry.Get(session.GameId).GetGameRoute(session.LocalRoleId));
        }
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        if (await _dialogs.ConfirmAsync(UiStrings.HistoryFinishTitle, UiStrings.HistoryFinishConfirm))
        {
            await _sessions.FinishAsync(_sessionId);
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (await _dialogs.ConfirmAsync(UiStrings.HistoryDeleteTitle, UiStrings.HistoryDeleteConfirm))
        {
            await _events.DeleteAsync(_sessionId);
            await _store.DeleteAsync(_sessionId);
            await _navigation.GoBackAsync();
        }
    }
}
