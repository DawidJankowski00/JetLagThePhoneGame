using System.Text.Json;
using TransitGames.Core.Games;
using TransitGames.Core.Geography;
using TransitGames.Core.Sessions;
using TransitGames.Core.Sync;
using TransitGames.Games.HideAndSeek.Questions;

namespace TransitGames.Games.HideAndSeek.Sync;

public sealed record AnswerPayload(Guid QuestionId, QuestionAnswer? Answer, bool Vetoed, DateTimeOffset AnsweredAt);

public sealed record QuestionRemovedPayload(Guid QuestionId);

public sealed record FoundPayload(DateTimeOffset At, GeoPoint? Location);

/// <summary>
/// Message types of Hide and Seek and builders for them. The hiders' station and position are never part of any
/// message; answers only reveal what the question asks.
/// </summary>
public static class HideAndSeekMessages
{
    public const string Question = "hs.question";
    public const string Answer = "hs.answer";
    public const string QuestionRemoved = "hs.question.removed";
    public const string CardPlayed = "hs.card";
    public const string Found = "hs.found";

    public static SyncMessage AskQuestion(IGameSession session, AskedQuestion question) =>
        Create(session, Question, SyncAudience.Everyone, question with { Answer = null, AnsweredAt = null, Vetoed = false }, question.AskedAt);

    /// <param name="correction">Seekers correcting their own record (entered by hand) — stays in their team.</param>
    public static SyncMessage AnswerQuestion(IGameSession session, AnswerPayload answer, bool correction = false) =>
        Create(session, Answer, correction ? SyncAudience.OwnTeam : SyncAudience.Everyone, answer, answer.AnsweredAt);

    public static SyncMessage RemoveQuestion(IGameSession session, Guid questionId, DateTimeOffset now) =>
        Create(session, QuestionRemoved, SyncAudience.OwnTeam, new QuestionRemovedPayload(questionId), now);

    public static SyncMessage PlayCard(IGameSession session, CardPlay play) =>
        Create(session, CardPlayed, SyncAudience.Everyone, play, play.PlayedAt);

    /// <summary>Only the seekers' position travels with "found" — the hiders' position is never sent.</summary>
    public static SyncMessage FoundHiders(IGameSession session, FoundPayload found) =>
        Create(session, Found, SyncAudience.Everyone, session.LocalRoleId == HideAndSeekRoles.Hider ? found with { Location = null } : found, found.At);

    public static T Read<T>(SyncMessage message) =>
        JsonSerializer.Deserialize<T>(message.PayloadJson, GameJson.Options)
        ?? throw new JsonException($"Empty payload of {message.Type}.");

    private static SyncMessage Create<T>(IGameSession session, string type, SyncAudience audience, T payload, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = session.Id,
        Type = type,
        SenderRoleId = session.LocalRoleId,
        Timestamp = at,
        Audience = audience,
        PayloadJson = JsonSerializer.Serialize(payload, GameJson.Options),
    };
}

/// <summary>Applies Hide and Seek messages (own echoes included) to the session. Every operation is idempotent.</summary>
public sealed class HideAndSeekMessageHandler : IGameMessageHandler
{
    public string GameId => HideAndSeekModule.GameId;

    public void Handle(IGameSession session, SyncMessage message)
    {
        if (session is not HideAndSeekSession game)
        {
            return;
        }

        switch (message.Type)
        {
            case HideAndSeekMessages.Question:
                game.ApplyQuestion(HideAndSeekMessages.Read<AskedQuestion>(message));
                break;
            case HideAndSeekMessages.Answer:
                var answer = HideAndSeekMessages.Read<AnswerPayload>(message);
                game.ApplyAnswer(answer.QuestionId, answer.Answer, answer.Vetoed, answer.AnsweredAt);
                break;
            case HideAndSeekMessages.QuestionRemoved:
                game.RemoveQuestion(HideAndSeekMessages.Read<QuestionRemovedPayload>(message).QuestionId);
                break;
            case HideAndSeekMessages.CardPlayed:
                game.ApplyCardPlayed(HideAndSeekMessages.Read<CardPlay>(message));
                break;
            case HideAndSeekMessages.Found:
                var found = HideAndSeekMessages.Read<FoundPayload>(message);
                game.ApplyFound(found.At, found.Location);
                break;
        }
    }
}
