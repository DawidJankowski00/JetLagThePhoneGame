using System.Text.Json;
using TransitGames.Core.Games;
using TransitGames.Core.Geography;
using TransitGames.Core.Sessions;
using TransitGames.Core.Sync;
using TransitGames.Games.Tag.Coins;

namespace TransitGames.Games.Tag.Sync;

/// <summary>A destination picked before the start (by the player or, when <paramref name="PlayerId"/> is another player, the host).</summary>
public sealed record DestinationPayload(Guid PlayerId, GeoPoint Location, string Name);

public sealed record StartPlayer(Guid Id, string Name, GeoPoint? Destination, string? DestinationName);

/// <summary>The host starts the game: the players, their destinations and the first runner, drawn on the host.</summary>
public sealed record StartPayload(DateTimeOffset StartedAt, GeoPoint StartPoint, IReadOnlyList<StartPlayer> Players, Guid RunnerId);

public sealed record RideStartPayload(int Turn, Guid RideId, TransportMode Mode, FareModel FareModel, int Upfront, double Multiplier, DateTimeOffset At);

public sealed record RideEndPayload(Guid RideId, double DistanceMeters, int TotalFare, DateTimeOffset At);

public sealed record CardDrawPayload(int Turn, Guid DrawId, string CardId, DateTimeOffset At);

/// <param name="Completed">True: challenge done; false: given up.</param>
public sealed record CardResolvePayload(Guid DrawId, bool Completed, DateTimeOffset At);

public sealed record CatchRequestPayload(int Turn, Guid RequestId, Guid ChaserId, DateTimeOffset At);

/// <summary>
/// The runner (or, <paramref name="Forced"/>, the host) confirms the catch. The next runner is drawn by the sender.
/// <paramref name="Point"/> is where the next turn starts: the runner's GPS, or the last known position when forced.
/// </summary>
public sealed record CatchPayload(int Turn, DateTimeOffset At, Guid? CatcherId, GeoPoint Point, Guid NextRunnerId, bool Forced);

public sealed record ArrivedPayload(int Turn, DateTimeOffset At);

/// <summary>End of the game after the time limit, with the runner's position that decides the winner.</summary>
public sealed record EndPayload(DateTimeOffset At, GeoPoint Point, EndPointSource Source);

public sealed record AdjustPayload(Guid Id, Guid PlayerId, int Amount, string Reason, DateTimeOffset At);

/// <summary>
/// Message types of Tag and builders for them. All go to everybody: the game state is the same on every device.
/// The runner's position is never part of a message except where it is public anyway: the place of a catch (the
/// chasers are there) and the end of the game. Chasers' positions are never sent.
/// </summary>
public static class TagMessages
{
    public const string Destination = "tag.destination";
    public const string Start = "tag.start";
    public const string RideStart = "tag.ride.start";
    public const string RideEnd = "tag.ride.end";
    public const string CardDraw = "tag.card.draw";
    public const string CardResolve = "tag.card.resolve";
    public const string CatchRequest = "tag.catch.request";
    public const string Catch = "tag.catch";
    public const string Arrived = "tag.arrived";
    public const string End = "tag.end";
    public const string Adjust = "tag.coins.adjust";

    /// <summary>Roles version of the lobby: all players join as chasers, the start makes the first change.</summary>
    public const int StartRolesVersion = 0;

    public static SyncMessage SetDestination(IGameSession session, DestinationPayload payload, DateTimeOffset at) =>
        Create(session, Destination, payload, at);

    /// <summary>Starts the game and makes the drawn player the runner (a role change for that device only).</summary>
    public static SyncMessage StartGame(IGameSession session, StartPayload payload) =>
        Create(session, Start, payload, payload.StartedAt) with
        {
            RoleChange = RoleChange.Assign(StartRolesVersion, new Dictionary<Guid, string> { [payload.RunnerId] = TagRoles.Runner }),
        };

    public static SyncMessage StartRide(IGameSession session, RideStartPayload payload) => Create(session, RideStart, payload, payload.At);

    public static SyncMessage EndRide(IGameSession session, RideEndPayload payload) => Create(session, RideEnd, payload, payload.At);

    public static SyncMessage DrawCard(IGameSession session, CardDrawPayload payload) => Create(session, CardDraw, payload, payload.At);

    public static SyncMessage ResolveCard(IGameSession session, CardResolvePayload payload) => Create(session, CardResolve, payload, payload.At);

    public static SyncMessage RequestCatch(IGameSession session, CatchRequestPayload payload) => Create(session, CatchRequest, payload, payload.At);

    /// <summary>
    /// The catch: the caught runner becomes a chaser, the next runner a runner. The server moves both devices between
    /// role groups at once, so the chasers see the new runner and the new runner no longer gets any positions.
    /// The roles version follows from the turn (the start is change 1, the catch of turn k change k + 2), so a late
    /// second confirmation of the same catch can never change the roles again.
    /// </summary>
    public static SyncMessage ConfirmCatch(IGameSession session, CatchPayload payload, Guid caughtRunner) =>
        Create(session, Catch, payload, payload.At) with
        {
            RoleChange = RoleChange.Assign(StartRolesVersion + 1 + payload.Turn, new Dictionary<Guid, string>
            {
                [caughtRunner] = TagRoles.Chaser,
                [payload.NextRunnerId] = TagRoles.Runner,
            }),
        };

    public static SyncMessage ReportArrival(IGameSession session, ArrivedPayload payload) => Create(session, Arrived, payload, payload.At);

    public static SyncMessage EndGame(IGameSession session, EndPayload payload) => Create(session, End, payload, payload.At);

    public static SyncMessage AdjustCoins(IGameSession session, AdjustPayload payload) => Create(session, Adjust, payload, payload.At);

    public static T Read<T>(SyncMessage message) =>
        JsonSerializer.Deserialize<T>(message.PayloadJson, GameJson.Options)
        ?? throw new JsonException($"Empty payload of {message.Type}.");

    private static SyncMessage Create<T>(IGameSession session, string type, T payload, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = session.Id,
        Type = type,
        SenderRoleId = session.LocalRoleId,
        Timestamp = at,
        Audience = SyncAudience.Everyone,
        PayloadJson = JsonSerializer.Serialize(payload, GameJson.Options),
        SenderDeviceId = session.LocalDeviceId,
    };
}

/// <summary>Applies Tag messages to the session. Idempotent: everything is keyed by turn index or id.</summary>
public sealed class TagMessageHandler : IGameMessageHandler
{
    public string GameId => TagModule.GameId;

    public void Handle(IGameSession session, SyncMessage message)
    {
        if (session is not TagSession tag || tag.Settings.IsLegacy)
        {
            return;
        }

        switch (message.Type)
        {
            case TagMessages.Destination:
                tag.ApplyDestination(TagMessages.Read<DestinationPayload>(message));
                break;
            case TagMessages.Start:
                tag.ApplyStart(TagMessages.Read<StartPayload>(message));
                break;
            case TagMessages.RideStart:
                tag.ApplyRideStart(TagMessages.Read<RideStartPayload>(message));
                break;
            case TagMessages.RideEnd:
                tag.ApplyRideEnd(TagMessages.Read<RideEndPayload>(message));
                break;
            case TagMessages.CardDraw:
                tag.ApplyCardDraw(TagMessages.Read<CardDrawPayload>(message));
                break;
            case TagMessages.CardResolve:
                tag.ApplyCardResolve(TagMessages.Read<CardResolvePayload>(message));
                break;
            case TagMessages.CatchRequest:
                tag.ApplyCatchRequest(TagMessages.Read<CatchRequestPayload>(message));
                break;
            case TagMessages.Catch:
                tag.ApplyCatch(TagMessages.Read<CatchPayload>(message));
                break;
            case TagMessages.Arrived:
                tag.ApplyArrived(TagMessages.Read<ArrivedPayload>(message));
                break;
            case TagMessages.End:
                tag.ApplyEnd(TagMessages.Read<EndPayload>(message));
                break;
            case TagMessages.Adjust:
                tag.ApplyAdjust(TagMessages.Read<AdjustPayload>(message));
                break;
        }
    }
}
