using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TransitGames.Core.Games;
using TransitGames.Core.Sync;
using TransitGames.Server.Storage;

namespace TransitGames.Server.Lobbies;

public sealed class LobbyException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string Code { get; } = code;

    public OnlineError ToError() => new(Code, Message);
}

/// <summary>An authenticated device (resolved from its token).</summary>
public sealed record DeviceIdentity(Guid DeviceId, Guid SessionId, string RoleId, string Name);

/// <summary>Result of publishing: <see cref="IsNew"/> is false for a retried message that was already stored.</summary>
public sealed record PublishResult(SequencedMessage Message, IReadOnlyList<string> RecipientRoles, bool IsNew);

/// <summary>
/// Games ("lobbies"), their devices and messages. The server is game-agnostic: it only knows role ids and the
/// location sharing rules sent by the host, and enforces those rules for <see cref="SyncMessageTypes.Location"/>.
/// </summary>
public sealed class LobbyService
{
    public const int MaxPlayerNameLength = 40;
    private const int MaxCodeAttempts = 20;

    private readonly ServerStore _store;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public LobbyService(ServerStore store, TimeProvider time)
    {
        _store = store;
        _time = time;
    }

    public async Task<LobbyTicket> CreateAsync(CreateLobbyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GameId) || request.Roles.Count == 0 || request.SessionId == Guid.Empty)
        {
            throw Invalid("Game id, roles and session id are required.");
        }

        EnsureRole(request.Roles, request.RoleId);
        var db = await _store.GetAsync();
        await _writeLock.WaitAsync();
        try
        {
            if (await db.Table<LobbyRow>().Where(l => l.SessionId == request.SessionId).CountAsync() > 0)
            {
                throw new LobbyException(StatusCodes.Status409Conflict, OnlineErrorCodes.LobbyExists, "This game is already online.");
            }

            var code = await NewCodeAsync();
            var now = _time.GetUtcNow().UtcTicks;
            await db.InsertAsync(new LobbyRow
            {
                Code = code,
                SessionId = request.SessionId,
                GameId = request.GameId,
                GameName = request.GameName,
                SettingsJson = request.SettingsJson,
                RolesJson = JsonSerializer.Serialize(request.Roles, GameJson.Options),
                SharingRulesJson = JsonSerializer.Serialize(request.SharingRules, GameJson.Options),
                CreatedAtTicks = now,
                UpdatedAtTicks = now,
            });
            return await AddDeviceAsync(code, request.SessionId, request.RoleId, request.PlayerName);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<LobbyInfo> GetInfoAsync(string code, IReadOnlyCollection<Guid> connectedDevices)
    {
        var lobby = await GetLobbyAsync(code);
        var db = await _store.GetAsync();
        var devices = await db.Table<DeviceRow>().Where(d => d.SessionId == lobby.SessionId).ToListAsync();
        return new LobbyInfo(
            lobby.Code,
            lobby.SessionId,
            lobby.GameId,
            lobby.GameName,
            Roles(lobby),
            devices.OrderBy(d => d.JoinedAtTicks)
                .Select(d => new SyncPeer(d.Id, d.Name, d.RoleId, connectedDevices.Contains(d.Id)))
                .ToList(),
            lobby.IsFinished);
    }

    public async Task<JoinLobbyResult> JoinAsync(string code, JoinLobbyRequest request)
    {
        var lobby = await GetLobbyAsync(code);
        if (lobby.IsFinished)
        {
            throw new LobbyException(StatusCodes.Status410Gone, OnlineErrorCodes.LobbyFinished, "This game has already finished.");
        }

        EnsureRole(Roles(lobby), request.RoleId);
        await _writeLock.WaitAsync();
        try
        {
            var ticket = await AddDeviceAsync(lobby.Code, lobby.SessionId, request.RoleId, request.PlayerName);
            return new JoinLobbyResult(ticket, lobby.GameId, lobby.SettingsJson);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<DeviceIdentity?> AuthenticateAsync(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var hash = Hash(token);
        var db = await _store.GetAsync();
        var device = await db.Table<DeviceRow>().Where(d => d.TokenHash == hash).FirstOrDefaultAsync();
        return device is null ? null : new DeviceIdentity(device.Id, device.SessionId, device.RoleId, device.Name);
    }

    public async Task<IReadOnlyList<SyncPeer>> GetPeersAsync(Guid sessionId, IReadOnlyCollection<Guid> connectedDevices)
    {
        var db = await _store.GetAsync();
        var devices = await db.Table<DeviceRow>().Where(d => d.SessionId == sessionId).ToListAsync();
        return devices.OrderBy(d => d.JoinedAtTicks)
            .Select(d => new SyncPeer(d.Id, d.Name, d.RoleId, connectedDevices.Contains(d.Id)))
            .ToList();
    }

    /// <summary>Stores a message from <paramref name="sender"/> and decides who may receive it.</summary>
    public async Task<PublishResult> PublishAsync(DeviceIdentity sender, SyncMessage message)
    {
        if (message.Id == Guid.Empty || string.IsNullOrWhiteSpace(message.Type))
        {
            throw Invalid("Message id and type are required.");
        }

        var lobby = await GetLobbyBySessionAsync(sender.SessionId);

        // Identity comes from the token, never from the client's claims.
        var stamped = message with
        {
            SessionId = sender.SessionId,
            SenderRoleId = sender.RoleId,
            SenderDeviceId = sender.DeviceId,
            SenderName = sender.Name,
        };
        var recipients = RecipientRoles(lobby, stamped);
        var db = await _store.GetAsync();

        await _writeLock.WaitAsync();
        try
        {
            var existing = await db.Table<MessageRow>().Where(m => m.MessageId == message.Id).FirstOrDefaultAsync();
            if (existing is not null)
            {
                return new PublishResult(ToSequenced(existing), Split(existing.RecipientRoles), IsNew: false);
            }

            var row = new MessageRow
            {
                MessageId = stamped.Id,
                SessionId = stamped.SessionId,
                SenderDeviceId = sender.DeviceId,
                Type = stamped.Type,
                RecipientRoles = Join(recipients),
                Json = JsonSerializer.Serialize(stamped, GameJson.Options),
            };
            await db.InsertAsync(row);

            lobby.UpdatedAtTicks = _time.GetUtcNow().UtcTicks;
            lobby.IsFinished |= stamped.Type == SyncMessageTypes.SessionFinished;
            await db.UpdateAsync(lobby);
            return new PublishResult(new SequencedMessage(row.Sequence, stamped), recipients, IsNew: true);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Messages the device may see, published by other devices after <paramref name="afterSequence"/>.</summary>
    public async Task<IReadOnlyList<SequencedMessage>> GetMessagesAsync(DeviceIdentity device, long afterSequence)
    {
        var db = await _store.GetAsync();
        var rows = await db.QueryAsync<MessageRow>(
            "SELECT * FROM messages WHERE SessionId = ? AND Sequence > ? AND SenderDeviceId <> ? AND RecipientRoles LIKE ? ORDER BY Sequence",
            device.SessionId, afterSequence, device.DeviceId, RoleLike(device.RoleId));
        return rows.Select(ToSequenced).ToList();
    }

    /// <summary>The newest location of every other device whose location <paramref name="device"/> may see.</summary>
    public async Task<IReadOnlyList<SequencedMessage>> GetLatestLocationsAsync(DeviceIdentity device)
    {
        var db = await _store.GetAsync();
        var rows = await db.QueryAsync<MessageRow>(
            """
            SELECT * FROM messages WHERE Sequence IN (
                SELECT MAX(Sequence) FROM messages
                WHERE SessionId = ? AND Type = ? AND SenderDeviceId <> ? AND RecipientRoles LIKE ?
                GROUP BY SenderDeviceId)
            ORDER BY Sequence
            """,
            device.SessionId, SyncMessageTypes.Location, device.DeviceId, RoleLike(device.RoleId));
        return rows.Select(ToSequenced).ToList();
    }

    /// <summary>Deletes games not updated since <paramref name="cutoff"/>. Returns the number deleted.</summary>
    public async Task<int> DeleteStaleAsync(DateTimeOffset cutoff)
    {
        var db = await _store.GetAsync();
        await _writeLock.WaitAsync();
        try
        {
            var stale = await db.Table<LobbyRow>().Where(l => l.UpdatedAtTicks < cutoff.UtcTicks).ToListAsync();
            foreach (var lobby in stale)
            {
                await db.ExecuteAsync("DELETE FROM messages WHERE SessionId = ?", lobby.SessionId);
                await db.ExecuteAsync("DELETE FROM devices WHERE SessionId = ?", lobby.SessionId);
                await db.DeleteAsync(lobby);
            }

            return stale.Count;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Location messages go to the sender's team and the viewers allowed by the game's rules only — whatever
    /// audience the client asked for. Other messages follow their audience.
    /// </summary>
    internal static IReadOnlyList<string> RecipientRoles(LobbyRow lobby, SyncMessage message)
    {
        if (message.Type == SyncMessageTypes.Location)
        {
            var rules = JsonSerializer.Deserialize<List<LocationSharingRule>>(lobby.SharingRulesJson, GameJson.Options) ?? [];
            return [message.SenderRoleId, .. rules.Where(r => r.SubjectRoleId == message.SenderRoleId).Select(r => r.ViewerRoleId).Distinct()];
        }

        return message.Audience == SyncAudience.OwnTeam
            ? [message.SenderRoleId]
            : Roles(lobby).Select(r => r.Id).ToList();
    }

    private async Task<LobbyTicket> AddDeviceAsync(string code, Guid sessionId, string roleId, string playerName)
    {
        var name = playerName.Trim();
        name = name.Length == 0 ? "?" : name[..Math.Min(name.Length, MaxPlayerNameLength)];
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var device = new DeviceRow
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            RoleId = roleId,
            Name = name,
            TokenHash = Hash(token),
            JoinedAtTicks = _time.GetUtcNow().UtcTicks,
        };
        var db = await _store.GetAsync();
        await db.InsertAsync(device);
        return new LobbyTicket(code, sessionId, device.Id, roleId, token);
    }

    private async Task<string> NewCodeAsync()
    {
        var db = await _store.GetAsync();
        for (var attempt = 0; attempt < MaxCodeAttempts; attempt++)
        {
            var code = new string(RandomNumberGenerator.GetItems<char>(OnlineProtocol.CodeAlphabet, OnlineProtocol.CodeLength));
            if (await db.FindAsync<LobbyRow>(code) is null)
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not generate a free game code.");
    }

    private async Task<LobbyRow> GetLobbyAsync(string code)
    {
        var db = await _store.GetAsync();
        return await db.FindAsync<LobbyRow>(OnlineProtocol.NormalizeCode(code))
            ?? throw new LobbyException(StatusCodes.Status404NotFound, OnlineErrorCodes.LobbyNotFound, "No game with this code.");
    }

    private async Task<LobbyRow> GetLobbyBySessionAsync(Guid sessionId)
    {
        var db = await _store.GetAsync();
        return await db.Table<LobbyRow>().Where(l => l.SessionId == sessionId).FirstOrDefaultAsync()
            ?? throw new LobbyException(StatusCodes.Status404NotFound, OnlineErrorCodes.LobbyNotFound, "The game no longer exists.");
    }

    private static IReadOnlyList<LobbyRole> Roles(LobbyRow lobby) =>
        JsonSerializer.Deserialize<List<LobbyRole>>(lobby.RolesJson, GameJson.Options) ?? [];

    private static void EnsureRole(IReadOnlyList<LobbyRole> roles, string roleId)
    {
        if (roles.All(r => r.Id != roleId))
        {
            throw new LobbyException(StatusCodes.Status400BadRequest, OnlineErrorCodes.UnknownRole, $"Unknown role '{roleId}'.");
        }
    }

    private static LobbyException Invalid(string message) =>
        new(StatusCodes.Status400BadRequest, OnlineErrorCodes.InvalidRequest, message);

    private static SequencedMessage ToSequenced(MessageRow row) =>
        new(row.Sequence, JsonSerializer.Deserialize<SyncMessage>(row.Json, GameJson.Options)!);

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Join(IEnumerable<string> roles) => "|" + string.Join("|", roles) + "|";

    private static IReadOnlyList<string> Split(string roles) => roles.Split('|', StringSplitOptions.RemoveEmptyEntries);

    private static string RoleLike(string roleId) => $"%|{roleId}|%";
}
