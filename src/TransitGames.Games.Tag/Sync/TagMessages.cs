using System.Text.Json;
using TransitGames.Core.Games;
using TransitGames.Core.Geography;
using TransitGames.Core.Sessions;
using TransitGames.Core.Sync;

namespace TransitGames.Games.Tag.Sync;

public sealed record CapturePayload(int Round, string TargetId, DateTimeOffset At);

/// <param name="Point">Where the round ended — only sent by the runners (see <see cref="TagMessages.EndRound"/>).</param>
/// <param name="NextStartsAt">Start of the next round; null when this was the last one.</param>
public sealed record RoundEndPayload(int Round, RoundEndReason Reason, DateTimeOffset EndedAt, GeoPoint? Point, DateTimeOffset? NextStartsAt);

public sealed record SpendPayload(int Round, Guid Id, int Amount, string Note, DateTimeOffset At);

/// <summary>
/// Message types of Tag and builders for them. The chasers' position is never part of any message; the runners'
/// position travels only as location pings (see TagModule.LocationSharing) and with a round end they report.
/// </summary>
public static class TagMessages
{
    public const string Capture = "tag.capture";
    public const string RoundEnd = "tag.round.end";
    public const string Spend = "tag.spend";

    public static SyncMessage CaptureTarget(IGameSession session, CapturePayload capture) =>
        Create(session, Capture, SyncAudience.Everyone, capture, capture.At);

    /// <summary>
    /// Ends the round for everybody and, when another round follows, swaps runners and chasers (the server moves the
    /// devices between role groups, so location sharing follows at once). A chasers' device leaves the position out.
    /// </summary>
    public static SyncMessage EndRound(IGameSession session, RoundEndPayload end)
    {
        var payload = session.LocalRoleId == TagRoles.Chaser ? end with { Point = null } : end;
        return Create(session, RoundEnd, SyncAudience.Everyone, payload, end.EndedAt) with
        {
            RoleChange = end.NextStartsAt is null ? null : RoleChange.Swap(session.RolesVersion, TagRoles.Runner, TagRoles.Chaser),
        };
    }

    /// <summary>Coins spent by the runners on fares; their own business.</summary>
    public static SyncMessage SpendCoins(IGameSession session, SpendPayload spend) =>
        Create(session, Spend, SyncAudience.OwnTeam, spend, spend.At);

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

/// <summary>Applies Tag messages to the session (idempotent: rounds and targets are keyed by index and id).</summary>
public sealed class TagMessageHandler : IGameMessageHandler
{
    public string GameId => TagModule.GameId;

    public void Handle(IGameSession session, SyncMessage message)
    {
        if (session is not TagSession tag)
        {
            return;
        }

        switch (message.Type)
        {
            case TagMessages.Capture:
                tag.ApplyCapture(TagMessages.Read<CapturePayload>(message));
                break;
            case TagMessages.RoundEnd:
                tag.ApplyRoundEnd(TagMessages.Read<RoundEndPayload>(message));
                break;
            case TagMessages.Spend:
                tag.ApplySpend(TagMessages.Read<SpendPayload>(message));
                break;
        }
    }
}
