using Microsoft.Extensions.Logging;
using TransitGames.Core.Geography;
using TransitGames.Core.Location;
using TransitGames.Core.Sessions;
using TransitGames.Core.Sync;
using TransitGames.Core.Timing;
using TransitGames.Geo.Areas;
using TransitGames.Games.HideAndSeek.Elimination;
using TransitGames.Games.HideAndSeek.Places;
using TransitGames.Games.HideAndSeek.Questions;
using TransitGames.Games.HideAndSeek.Sync;

namespace TransitGames.Games.HideAndSeek.Maui.Services;

/// <summary>
/// Shared by the Hide and Seek screens: the current session, its parsed area, the elimination result (computed
/// off the UI thread and cached per question list) and every action, published through <see cref="IGameSyncService"/>
/// — the state changes when the message comes back (immediately for own messages), online or offline alike.
/// </summary>
public sealed class HideAndSeekController
{
    private readonly GameSessionManager _sessions;
    private readonly IGameSyncService _sync;
    private readonly ILocationTracker _location;
    private readonly PlaceCatalog _places;
    private readonly IClock _clock;
    private readonly ILogger<HideAndSeekController> _logger;
    private readonly SemaphoreSlim _computeLock = new(1, 1);
    private (Guid SessionId, GameArea Area)? _area;
    private (string Key, EliminationResult Result)? _elimination;

    public HideAndSeekController(
        GameSessionManager sessions,
        IGameSyncService sync,
        ILocationTracker location,
        PlaceCatalog places,
        IClock clock,
        ILogger<HideAndSeekController> logger)
    {
        _sessions = sessions;
        _sync = sync;
        _location = location;
        _places = places;
        _clock = clock;
        _logger = logger;
    }

    public HideAndSeekSession? Session => _sessions.Current as HideAndSeekSession;

    public DateTimeOffset Now => _clock.UtcNow;

    /// <summary>Online game: questions and answers travel by themselves; offline they are entered by hand.</summary>
    public bool IsOnline => _sync.IsOnline;

    public GameArea? Area
    {
        get
        {
            if (Session is not { Settings.Area: { } definition } session)
            {
                return null;
            }

            if (_area?.SessionId != session.Id)
            {
                _area = (session.Id, GameArea.FromDefinition(definition));
            }

            return _area.Value.Area;
        }
    }

    /// <summary>Applies all answers to the area. Cached: recomputed only when the questions change.</summary>
    public async Task<EliminationResult?> ComputeAsync()
    {
        if (Session is not { } session || Area is not { } area)
        {
            return null;
        }

        var questions = session.Questions.ToList();
        var key = session.Id + "|" + string.Join(";", questions.Select(q => $"{q.Id}:{q.Answer}:{q.Vetoed}"));
        await _computeLock.WaitAsync();
        try
        {
            if (_elimination?.Key == key)
            {
                return _elimination.Value.Result;
            }

            var result = await Task.Run(() => EliminationEngine.Compute(area, session.Settings.Stations, session.Settings.HidingZoneRadiusMeters, questions));
            _elimination = (key, result);
            return result;
        }
        finally
        {
            _computeLock.Release();
        }
    }

    /// <summary>This device's position: the running tracker's last fix, a fresh fix, or null.</summary>
    public async Task<GeoPoint?> CurrentLocationAsync()
    {
        if (_location.LastLocation is { } last && Now - last.Timestamp < TimeSpan.FromMinutes(2))
        {
            return last.Point;
        }

        try
        {
            return (await _location.GetCurrentLocationAsync())?.Point ?? _location.LastLocation?.Point;
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or FeatureNotEnabledException or PermissionException)
        {
            _logger.LogInformation(ex, "No location for the question");
            return _location.LastLocation?.Point;
        }
    }

    /// <summary>Where the hiders are for answer hints: their GPS, else the centre of their chosen station.</summary>
    public async Task<GeoPoint?> HiderLocationAsync() =>
        await CurrentLocationAsync()
        ?? Session?.Settings.Stations.FirstOrDefault(s => s.Id == Session.HiderStationId)?.Location;

    public Task<IReadOnlyList<Place>> PlacesAsync(string categoryId) =>
        Session is { } session && Area is { } area
            ? _places.GetAsync(area, session.Settings.Stations, categoryId)
            : Task.FromResult<IReadOnlyList<Place>>([]);

    public Task AskAsync(QuestionSpec spec) => Publish(s => HideAndSeekMessages.AskQuestion(s, new AskedQuestion
    {
        Id = Guid.NewGuid(),
        Spec = spec,
        AskedAt = Now,
    }));

    /// <param name="correction">Seekers entering/correcting an answer by hand (stays in their team).</param>
    public Task AnswerAsync(Guid questionId, QuestionAnswer? answer, bool veto = false, bool correction = false)
    {
        if (veto && Session is { } session && !session.UseVeto())
        {
            return Task.CompletedTask;
        }

        return Publish(s => HideAndSeekMessages.AnswerQuestion(s, new AnswerPayload(questionId, answer, veto, Now), correction));
    }

    public Task RemoveAsync(Guid questionId) => Publish(s => HideAndSeekMessages.RemoveQuestion(s, questionId, Now));

    public Task PlayCardAsync(int handIndex)
    {
        if (Session is not { } session)
        {
            return Task.CompletedTask;
        }

        var play = session.PlayCard(handIndex, Now);
        return play is null ? Task.CompletedTask : Publish(s => HideAndSeekMessages.PlayCard(s, play));
    }

    public async Task FoundAsync()
    {
        var location = Session?.IsHider == true ? null : await CurrentLocationAsync();
        await Publish(s => HideAndSeekMessages.FoundHiders(s, new FoundPayload(Now, location)));
    }

    private Task Publish(Func<HideAndSeekSession, SyncMessage> build) =>
        Session is { } session ? _sync.PublishAsync(build(session)) : Task.CompletedTask;
}
