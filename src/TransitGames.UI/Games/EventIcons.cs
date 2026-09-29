using TransitGames.Core.Events;

namespace TransitGames.UI.Games;

/// <summary>Timeline icons of the common event types (<see cref="GameEventTypes"/>).</summary>
public static class EventIcons
{
    public const string Default = "•";

    public static string For(string eventType) => eventType switch
    {
        GameEventTypes.SessionStarted => "▶️",
        GameEventTypes.SessionFinished => "⏹️",
        GameEventTypes.RoundStarted => "🔄",
        GameEventTypes.RoundEnded => "⏱️",
        GameEventTypes.QuestionAsked => "❓",
        GameEventTypes.QuestionAnswered => "💬",
        GameEventTypes.Caught => "🙌",
        GameEventTypes.TargetCaptured => "🎯",
        GameEventTypes.LocationPing => "📍",
        GameEventTypes.CardPlayed => "🃏",
        GameEventTypes.Note => "📝",
        _ => Default,
    };
}
