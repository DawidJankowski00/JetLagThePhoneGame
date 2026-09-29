namespace TransitGames.Core.Events;

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
