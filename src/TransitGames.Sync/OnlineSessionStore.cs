using System.Text.Json;
using SQLite;
using TransitGames.Core.Games;
using TransitGames.Core.Persistence;
using TransitGames.Core.Sync;

namespace TransitGames.Sync;

/// <summary>How this device takes part in an online game.</summary>
public sealed record OnlineLink(
    Guid SessionId,
    string ServerUrl,
    string Code,
    Guid DeviceId,
    string RoleId,
    string PlayerName,
    string Token,
    long LastSequence)
{
    public static OnlineLink From(string serverUrl, LobbyTicket ticket, string playerName) =>
        new(ticket.SessionId, serverUrl, ticket.Code, ticket.DeviceId, ticket.RoleId, playerName, ticket.Token, 0);
}

[Table("online_links")]
internal sealed class OnlineLinkRow
{
    [PrimaryKey]
    public Guid SessionId { get; set; }

    public string ServerUrl { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public Guid DeviceId { get; set; }
    public string RoleId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public long LastSequence { get; set; }
}

[Table("sync_outbox")]
internal sealed class OutboxRow
{
    [PrimaryKey, AutoIncrement]
    public long Id { get; set; }

    [Indexed]
    public Guid SessionId { get; set; }

    public string Json { get; set; } = string.Empty;
}

/// <summary>
/// Online links of sessions and the outbox of messages not yet accepted by the server. Both live in the app's
/// SQLite database, so an app killed in a tunnel still sends its messages after the restart.
/// </summary>
public sealed class OnlineSessionStore
{
    private readonly AppDatabase _database;

    public OnlineSessionStore(AppDatabase database) => _database = database;

    public async Task SaveLinkAsync(OnlineLink link)
    {
        var db = await _database.GetAsync<OnlineLinkRow>().ConfigureAwait(false);
        await db.InsertOrReplaceAsync(new OnlineLinkRow
        {
            SessionId = link.SessionId,
            ServerUrl = link.ServerUrl,
            Code = link.Code,
            DeviceId = link.DeviceId,
            RoleId = link.RoleId,
            PlayerName = link.PlayerName,
            Token = link.Token,
            LastSequence = link.LastSequence,
        }).ConfigureAwait(false);
    }

    public async Task<OnlineLink?> GetLinkAsync(Guid sessionId)
    {
        var db = await _database.GetAsync<OnlineLinkRow>().ConfigureAwait(false);
        var row = await db.FindAsync<OnlineLinkRow>(sessionId).ConfigureAwait(false);
        return row is null
            ? null
            : new OnlineLink(row.SessionId, row.ServerUrl, row.Code, row.DeviceId, row.RoleId, row.PlayerName, row.Token, row.LastSequence);
    }

    public async Task SetLastSequenceAsync(Guid sessionId, long sequence)
    {
        var db = await _database.GetAsync<OnlineLinkRow>().ConfigureAwait(false);
        await db.ExecuteAsync("UPDATE online_links SET LastSequence = ? WHERE SessionId = ? AND LastSequence < ?", sequence, sessionId, sequence)
            .ConfigureAwait(false);
    }

    public async Task EnqueueAsync(SyncMessage message)
    {
        var db = await _database.GetAsync<OutboxRow>().ConfigureAwait(false);
        await db.InsertAsync(new OutboxRow { SessionId = message.SessionId, Json = JsonSerializer.Serialize(message, GameJson.Options) })
            .ConfigureAwait(false);
    }

    /// <summary>Queued messages of the session, oldest first.</summary>
    public async Task<IReadOnlyList<(long Id, SyncMessage Message)>> GetOutboxAsync(Guid sessionId)
    {
        var db = await _database.GetAsync<OutboxRow>().ConfigureAwait(false);
        var rows = await db.Table<OutboxRow>().Where(r => r.SessionId == sessionId).OrderBy(r => r.Id).ToListAsync().ConfigureAwait(false);
        return rows.Select(r => (r.Id, JsonSerializer.Deserialize<SyncMessage>(r.Json, GameJson.Options)!)).ToList();
    }

    public async Task RemoveFromOutboxAsync(long id)
    {
        var db = await _database.GetAsync<OutboxRow>().ConfigureAwait(false);
        await db.DeleteAsync<OutboxRow>(id).ConfigureAwait(false);
    }
}
