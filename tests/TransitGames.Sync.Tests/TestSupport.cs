using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using TransitGames.Core.Games;
using TransitGames.Core.Geography;
using TransitGames.Core.Persistence;
using TransitGames.Core.Sync;

namespace TransitGames.Sync.Tests;

internal static class TestGame
{
    public const string Hider = "hider";
    public const string Seeker = "seeker";

    public static CreateLobbyRequest CreateRequest(Guid sessionId, string roleId = Hider, string player = "Anna") => new(
        sessionId,
        "hide-and-seek",
        "Hide and Seek",
        """{"hidingZoneRadiusMeters":500}""",
        [new LobbyRole(Hider, "Hider"), new LobbyRole(Seeker, "Seeker")],
        [new LocationSharingRule(Hider, Seeker)], // hiders see seekers, never the reverse
        roleId,
        player);

    public static SyncMessage Message(string type, SyncAudience audience = SyncAudience.Everyone, string payload = "{}", string claimedRole = "") => new()
    {
        Id = Guid.NewGuid(),
        SessionId = Guid.Empty, // stamped by the transport and again by the server
        Type = type,
        SenderRoleId = claimedRole,
        Timestamp = DateTimeOffset.UtcNow,
        Audience = audience,
        PayloadJson = payload,
    };

    public static SyncMessage Location(double lat, double lon) => Message(
        SyncMessageTypes.Location,
        payload: JsonSerializer.Serialize(new LocationPayload(new GeoPoint(lat, lon), DateTimeOffset.UtcNow, 5), GameJson.Options));
}

/// <summary>The real server in memory, with its own temporary database.</summary>
public sealed class ServerFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"transitgames-server-{Guid.NewGuid():N}.db");

    public const string Url = "http://localhost";

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("Storage:DatabasePath", _databasePath);

    public OnlineServerClient CreateRestClient() => new(CreateClient());

    public OnlineConnectionOptions ConnectionOptions() => new()
    {
        ConfigureHttp = http =>
        {
            http.HttpMessageHandlerFactory = _ => Server.CreateHandler();
            http.Transports = HttpTransportType.LongPolling;
        },
        RetryDelays = [TimeSpan.Zero, TimeSpan.FromMilliseconds(200)],
    };

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        TryDelete(_databasePath);
    }

    internal static void TryDelete(string path)
    {
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // SQLite may still hold the file briefly; temp files are cleaned up by the OS.
            }
        }
    }
}

/// <summary>One phone: its own app database (links + outbox) and transport.</summary>
internal sealed class TestDevice : IAsyncDisposable
{
    private readonly ConcurrentQueue<SyncMessage> _received = new();

    public TestDevice(OnlineConnectionOptions options, string? databasePath = null)
    {
        DatabasePath = databasePath ?? Path.Combine(Path.GetTempPath(), $"transitgames-device-{Guid.NewGuid():N}.db");
        Database = new AppDatabase(new DatabaseOptions(DatabasePath));
        Store = new OnlineSessionStore(Database);
        Sync = new SignalRGameSyncService(Store, options);
        Sync.MessageReceived += (_, m) => _received.Enqueue(m);
    }

    public string DatabasePath { get; }

    public AppDatabase Database { get; }

    public OnlineSessionStore Store { get; }

    public SignalRGameSyncService Sync { get; }

    public IReadOnlyList<SyncMessage> Received => [.. _received];

    public IReadOnlyList<SyncMessage> ReceivedFromOthers(Guid ownDeviceId) =>
        Received.Where(m => m.SenderDeviceId != ownDeviceId).ToList();

    public async Task JoinAsync(LobbyTicket ticket, string player)
    {
        await Store.SaveLinkAsync(OnlineLink.From(ServerFactory.Url, ticket, player));
        await Sync.ConnectAsync(ticket.SessionId, ticket.RoleId);
    }

    public Task WaitUntilConnectedAsync() => Eventually.Async(() => Sync.State == SyncConnectionState.Connected, "connected");

    public Task<SyncMessage> WaitForAsync(Func<SyncMessage, bool> predicate, string what) =>
        Eventually.Async(() => Received.FirstOrDefault(predicate), what);

    public async ValueTask DisposeAsync()
    {
        await Sync.DisposeAsync();
        await Database.DisposeAsync();
        ServerFactory.TryDelete(DatabasePath);
    }
}

internal static class Eventually
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static async Task Async(Func<bool> condition, string what)
    {
        await Async(() => condition() ? (object)true : null, what);
    }

    public static async Task<T> Async<T>(Func<T?> probe, string what)
        where T : class
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (probe() is { } value)
            {
                return value;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Timed out waiting for: {what}");
    }

    /// <summary>Gives in-flight deliveries a moment, for asserting that something did NOT arrive.</summary>
    public static Task QuietPeriodAsync() => Task.Delay(TimeSpan.FromMilliseconds(600));
}
