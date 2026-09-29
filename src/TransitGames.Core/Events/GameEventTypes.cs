namespace TransitGames.Core.Events;

/// <summary>Well-known keys of <see cref="GameEvent.Data"/>.</summary>
public static class GameEventData
{
    /// <summary>
    /// The role of this device from the event's time on (games whose roles change during play). Track exports split
    /// the recording into segments by it; an empty value means no role (e.g. after the end of the game).
    /// </summary>
    public const string LocalRole = "localRole";
}

public static class GameEventTypes
{
    public const string SessionStarted = "session.started";
    public const string SessionFinished = "session.finished";
    public const string RoundStarted = "round.started";
    public const string RoundEnded = "round.ended";
    public const string QuestionAsked = "question.asked";
    public const string QuestionAnswered = "question.answered";
    public const string Caught = "player.caught";
    public const string TargetCaptured = "target.captured";
    public const string LocationPing = "location.ping";
    public const string CardPlayed = "card.played";
    public const string Note = "note";
}
