using TransitGames.Core.Games;
using TransitGames.Core.Geography;

namespace TransitGames.Core.Sync;

/// <summary>Wire contract between the app and TransitGames.Server. Bump <see cref="Version"/> on breaking changes.</summary>
public static class OnlineProtocol
{
    public const int Version = 3;

    public const string HubPath = "/hubs/game";

    /// <summary>Server → client: a <see cref="SequencedMessage"/> for this device.</summary>
    public const string ReceiveMethod = "Receive";

    /// <summary>Server → client: the full <see cref="SyncPeer"/> list of the game.</summary>
    public const string PeersMethod = "Peers";

    /// <summary>Client → server: publish a <see cref="SyncMessage"/>; returns its sequence number.</summary>
    public const string PublishMethod = "Publish";

    /// <summary>Client → server: messages for this device after a sequence number (catch-up after a disconnect).</summary>
    public const string SyncMethod = "Sync";

    /// <summary>Client → server: the newest location of every device whose location this device may see.</summary>
    public const string LatestLocationsMethod = "LatestLocations";

    /// <summary>Alphabet of game codes: no 0/O, 1/I/L, so codes can be read out over the phone.</summary>
    public const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public const int CodeLength = 6;

    public static string NormalizeCode(string code) =>
        new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}

/// <summary>Message types handled by the app itself; games define their own types.</summary>
public static class SyncMessageTypes
{
    /// <summary>
    /// Position of the sending device. The server routes it only to the sender's team and to the roles allowed
    /// by the game's <see cref="LocationSharingPolicy"/>, whatever the message's audience says.
    /// </summary>
    public const string Location = "location";

    /// <summary>A device finished the game; it ends for everybody.</summary>
    public const string SessionFinished = "session.finished";
}

/// <summary>Payload of <see cref="SyncMessageTypes.Location"/>.</summary>
public sealed record LocationPayload(GeoPoint Point, DateTimeOffset Timestamp, double? AccuracyMeters);

public sealed record LobbyRole(string Id, string DisplayName);

public sealed record ServerInfo(string Name, string Version, int ProtocolVersion);

/// <summary>Creates an online game for a session that already exists on the host device.</summary>
public sealed record CreateLobbyRequest(
    Guid SessionId,
    string GameId,
    string GameName,
    string SettingsJson,
    IReadOnlyList<LobbyRole> Roles,
    IReadOnlyList<LocationSharingRule> SharingRules,
    string RoleId,
    string PlayerName);

public sealed record JoinLobbyRequest(string RoleId, string PlayerName);

/// <summary>Public description of a game, shown before joining.</summary>
public sealed record LobbyInfo(
    string Code,
    Guid SessionId,
    string GameId,
    string GameName,
    IReadOnlyList<LobbyRole> Roles,
    IReadOnlyList<SyncPeer> Players,
    bool IsFinished);

/// <summary>Credentials of one device in one game. The token authenticates the SignalR connection.</summary>
/// <param name="RolesVersion">Roles version <paramref name="RoleId"/> belongs to (non-zero when joining after roles changed).</param>
public sealed record LobbyTicket(string Code, Guid SessionId, Guid DeviceId, string RoleId, string Token, int RolesVersion = 0);

public sealed record JoinLobbyResult(LobbyTicket Ticket, string GameId, string SettingsJson);

/// <summary>A message with its server-assigned, strictly increasing sequence number.</summary>
public sealed record SequencedMessage(long Sequence, SyncMessage Message);

/// <summary>Error body of a rejected REST call.</summary>
public sealed record OnlineError(string Code, string Message);

public static class OnlineErrorCodes
{
    public const string LobbyNotFound = "lobby_not_found";
    public const string LobbyExists = "lobby_exists";
    public const string LobbyFinished = "lobby_finished";
    public const string UnknownRole = "unknown_role";
    public const string InvalidRequest = "invalid_request";
}
