using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TransitGames.Core.Sync;

namespace TransitGames.Sync.Tests;

public sealed class LobbyApiTests : IAsyncLifetime
{
    private readonly ServerFactory _server = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Create_get_and_join_a_game_by_code()
    {
        var api = _server.CreateRestClient();
        var sessionId = Guid.NewGuid();

        var host = await api.CreateLobbyAsync(ServerFactory.Url, TestGame.CreateRequest(sessionId));
        Assert.Equal(OnlineProtocol.CodeLength, host.Code.Length);
        Assert.All(host.Code, c => Assert.Contains(c, OnlineProtocol.CodeAlphabet));
        Assert.Equal(sessionId, host.SessionId);

        // Codes are read out loud or typed: case and separators do not matter.
        var info = await api.GetLobbyAsync(ServerFactory.Url, $" {host.Code[..3].ToLowerInvariant()}-{host.Code[3..]} ");
        Assert.Equal("Hide and Seek", info.GameName);
        Assert.Equal([TestGame.Hider, TestGame.Seeker], info.Roles.Select(r => r.Id));
        var hostPeer = Assert.Single(info.Players);
        Assert.Equal(("Anna", TestGame.Hider, false), (hostPeer.Name, hostPeer.RoleId, hostPeer.IsConnected));

        var joined = await api.JoinLobbyAsync(ServerFactory.Url, host.Code, new JoinLobbyRequest(TestGame.Seeker, "Bartek"));
        Assert.Equal(sessionId, joined.Ticket.SessionId);
        Assert.Equal("hide-and-seek", joined.GameId);
        Assert.Contains("hidingZoneRadiusMeters", joined.SettingsJson);
        Assert.NotEqual(host.Token, joined.Ticket.Token);
        Assert.Equal(2, (await api.GetLobbyAsync(ServerFactory.Url, host.Code)).Players.Count);
    }

    [Fact]
    public async Task Errors_have_codes()
    {
        var api = _server.CreateRestClient();
        var sessionId = Guid.NewGuid();
        var host = await api.CreateLobbyAsync(ServerFactory.Url, TestGame.CreateRequest(sessionId));

        var notFound = await Assert.ThrowsAsync<OnlineException>(() => api.GetLobbyAsync(ServerFactory.Url, "ZZZZZZ"));
        Assert.Equal(OnlineErrorCodes.LobbyNotFound, notFound.Code);

        var unknownRole = await Assert.ThrowsAsync<OnlineException>(
            () => api.JoinLobbyAsync(ServerFactory.Url, host.Code, new JoinLobbyRequest("referee", "X")));
        Assert.Equal(OnlineErrorCodes.UnknownRole, unknownRole.Code);

        var duplicate = await Assert.ThrowsAsync<OnlineException>(
            () => api.CreateLobbyAsync(ServerFactory.Url, TestGame.CreateRequest(sessionId)));
        Assert.Equal(OnlineErrorCodes.LobbyExists, duplicate.Code);
    }

    [Fact]
    public async Task Server_info_reports_the_protocol_version()
    {
        var info = await _server.CreateRestClient().GetInfoAsync(ServerFactory.Url);
        Assert.Equal(OnlineProtocol.Version, info.ProtocolVersion);
    }

    [Fact]
    public async Task Unreachable_server_is_a_network_error()
    {
        var api = new OnlineServerClient(new HttpClient { Timeout = TimeSpan.FromSeconds(5) });
        var error = await Assert.ThrowsAsync<OnlineException>(() => api.GetInfoAsync("http://127.0.0.1:1"));
        Assert.Equal(OnlineException.NetworkError, error.Code);
    }

    [Theory]
    [InlineData("10.8.0.1:5080", "http://10.8.0.1:5080")]
    [InlineData(" http://games.lan/ ", "http://games.lan")]
    [InlineData("https://example.org/path", "https://example.org")]
    [InlineData("", null)]
    [InlineData("ftp://x", null)]
    public void Server_addresses_are_normalized(string input, string? expected) =>
        Assert.Equal(expected, OnlineServerUrl.Normalize(input));
}

public sealed class OnlineSyncTests : IAsyncLifetime
{
    private readonly ServerFactory _server = new();
    private readonly List<TestDevice> _devices = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var device in _devices)
        {
            await device.DisposeAsync();
        }

        await _server.DisposeAsync();
    }

    private TestDevice NewDevice(string? databasePath = null)
    {
        var device = new TestDevice(_server.ConnectionOptions(), databasePath);
        _devices.Add(device);
        return device;
    }

    private async Task<(LobbyTicket Hider, LobbyTicket Seeker)> CreateGameAsync()
    {
        var api = _server.CreateRestClient();
        var hider = await api.CreateLobbyAsync(ServerFactory.Url, TestGame.CreateRequest(Guid.NewGuid()));
        var seeker = await api.JoinLobbyAsync(ServerFactory.Url, hider.Code, new JoinLobbyRequest(TestGame.Seeker, "Bartek"));
        return (hider, seeker.Ticket);
    }

    [Fact]
    public async Task Messages_reach_the_other_team_and_are_echoed_locally()
    {
        var (hiderTicket, seekerTicket) = await CreateGameAsync();
        var hider = NewDevice();
        var seeker = NewDevice();
        await hider.JoinAsync(hiderTicket, "Anna");
        await seeker.JoinAsync(seekerTicket, "Bartek");
        await hider.WaitUntilConnectedAsync();
        await seeker.WaitUntilConnectedAsync();

        var question = TestGame.Message("question.asked", payload: """{"radius":1000}""");
        await seeker.Sync.PublishAsync(question);

        // Echo on the sender, stamped with its identity.
        var echo = await seeker.WaitForAsync(m => m.Id == question.Id, "echo");
        Assert.Equal(seekerTicket.DeviceId, echo.SenderDeviceId);

        var received = await hider.WaitForAsync(m => m.Id == question.Id, "question on hider");
        Assert.Equal(TestGame.Seeker, received.SenderRoleId);
        Assert.Equal("Bartek", received.SenderName);
        Assert.Equal(hiderTicket.SessionId, received.SessionId);
        Assert.Equal("""{"radius":1000}""", received.PayloadJson);

        await Eventually.QuietPeriodAsync();
        Assert.Single(seeker.Received, m => m.Id == question.Id); // not delivered back a second time
    }

    [Fact]
    public async Task Location_is_shared_one_way_only()
    {
        var (hiderTicket, seekerTicket) = await CreateGameAsync();
        var hider = NewDevice();
        var seeker = NewDevice();
        await hider.JoinAsync(hiderTicket, "Anna");
        await seeker.JoinAsync(seekerTicket, "Bartek");
        await hider.WaitUntilConnectedAsync();
        await seeker.WaitUntilConnectedAsync();

        // Hider → seekers must never arrive, even when the (buggy or malicious) client asks for "everyone".
        await hider.Sync.PublishAsync(TestGame.Location(52.23, 21.01));

        // Seeker → hider is allowed.
        var seekerLocation = TestGame.Location(52.24, 21.02);
        await seeker.Sync.PublishAsync(seekerLocation);
        await hider.WaitForAsync(m => m.Id == seekerLocation.Id, "seeker location on hider");

        await Eventually.QuietPeriodAsync();
        Assert.Empty(seeker.ReceivedFromOthers(seekerTicket.DeviceId));
    }

    [Fact]
    public async Task Server_ignores_the_role_claimed_by_the_client()
    {
        var (hiderTicket, seekerTicket) = await CreateGameAsync();
        var seeker = NewDevice();
        await seeker.JoinAsync(seekerTicket, "Bartek");
        await seeker.WaitUntilConnectedAsync();

        // A raw client with the hider's token pretends to be a seeker to leak the hider's position.
        await using var raw = new HubConnectionBuilder()
            .WithUrl(ServerFactory.Url + OnlineProtocol.HubPath, http =>
            {
                http.AccessTokenProvider = () => Task.FromResult<string?>(hiderTicket.Token);
                http.HttpMessageHandlerFactory = _ => _server.Server.CreateHandler();
                http.Transports = HttpTransportType.LongPolling;
            })
            .AddJsonProtocol(o => OnlineJson.Configure(o.PayloadSerializerOptions))
            .Build();
        await raw.StartAsync();
        var spoofed = TestGame.Location(52.23, 21.01) with { SenderRoleId = TestGame.Seeker, SessionId = hiderTicket.SessionId };
        await raw.InvokeAsync<long>(OnlineProtocol.PublishMethod, spoofed);

        await Eventually.QuietPeriodAsync();
        Assert.Empty(seeker.ReceivedFromOthers(seekerTicket.DeviceId));
    }

    [Fact]
    public async Task Invalid_token_cannot_connect()
    {
        await using var raw = new HubConnectionBuilder()
            .WithUrl(ServerFactory.Url + OnlineProtocol.HubPath, http =>
            {
                http.AccessTokenProvider = () => Task.FromResult<string?>("not-a-token");
                http.HttpMessageHandlerFactory = _ => _server.Server.CreateHandler();
                http.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => raw.StartAsync());
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task Own_team_messages_stay_in_the_team()
    {
        var (hiderTicket, seekerTicket) = await CreateGameAsync();
        var api = _server.CreateRestClient();
        var secondSeekerTicket = (await api.JoinLobbyAsync(ServerFactory.Url, hiderTicket.Code, new JoinLobbyRequest(TestGame.Seeker, "Celina"))).Ticket;
        var hider = NewDevice();
        var seeker = NewDevice();
        var secondSeeker = NewDevice();
        await hider.JoinAsync(hiderTicket, "Anna");
        await seeker.JoinAsync(seekerTicket, "Bartek");
        await secondSeeker.JoinAsync(secondSeekerTicket, "Celina");
        await Task.WhenAll(hider.WaitUntilConnectedAsync(), seeker.WaitUntilConnectedAsync(), secondSeeker.WaitUntilConnectedAsync());

        var plan = TestGame.Message("team.note", SyncAudience.OwnTeam);
        await seeker.Sync.PublishAsync(plan);

        await secondSeeker.WaitForAsync(m => m.Id == plan.Id, "team note on the other seeker");
        await Eventually.QuietPeriodAsync();
        Assert.DoesNotContain(hider.Received, m => m.Id == plan.Id);
    }

    [Fact]
    public async Task Missed_messages_are_caught_up_in_order_after_reconnecting()
    {
        var (hiderTicket, seekerTicket) = await CreateGameAsync();
        var hider = NewDevice();
        await hider.JoinAsync(hiderTicket, "Anna");
        await hider.WaitUntilConnectedAsync();

        var seekerDatabase = Path.Combine(Path.GetTempPath(), $"transitgames-device-{Guid.NewGuid():N}.db");
        var seeker = NewDevice(seekerDatabase);
        await seeker.JoinAsync(seekerTicket, "Bartek");
        await seeker.WaitUntilConnectedAsync();
        var first = TestGame.Message("answer", payload: "1");
        await hider.Sync.PublishAsync(first);
        await seeker.WaitForAsync(m => m.Id == first.Id, "first answer");

        // The seeker's phone dies (e.g. app killed in the metro) while the hider keeps answering.
        await seeker.Sync.DisconnectAsync();
        var missed = Enumerable.Range(2, 3).Select(i => TestGame.Message("answer", payload: i.ToString())).ToList();
        foreach (var message in missed)
        {
            await hider.Sync.PublishAsync(message);
        }

        // App restarted: new transport over the same device database.
        var restarted = new SignalRGameSyncService(seeker.Store, _server.ConnectionOptions());
        var received = new List<SyncMessage>();
        restarted.MessageReceived += (_, m) => { lock (received) received.Add(m); };
        await restarted.ConnectAsync(seekerTicket.SessionId, TestGame.Seeker);
        await Eventually.Async(() => { lock (received) return received.Count >= 3; }, "catch-up");
        await Eventually.QuietPeriodAsync();

        lock (received)
        {
            // Exactly the missed ones, once, in order — not the first answer again.
            Assert.Equal(["2", "3", "4"], received.Select(m => m.PayloadJson));
        }

        await restarted.DisposeAsync();
    }

    [Fact]
    public async Task Messages_published_offline_are_sent_after_connecting()
    {
        var (hiderTicket, seekerTicket) = await CreateGameAsync();
        var hider = NewDevice();
        await hider.JoinAsync(hiderTicket, "Anna");
        await hider.WaitUntilConnectedAsync();

        // The seeker has no network: its connection cannot reach the server.
        var offlineOptions = new OnlineConnectionOptions
        {
            ConfigureHttp = http =>
            {
                http.HttpMessageHandlerFactory = _ => new FailingHandler();
                http.Transports = HttpTransportType.LongPolling;
            },
            RetryDelays = [TimeSpan.FromMinutes(5)],
        };
        var seeker = new TestDevice(offlineOptions);
        _devices.Add(seeker);
        await seeker.JoinAsync(seekerTicket, "Bartek");
        Assert.Equal(SyncConnectionState.Reconnecting, seeker.Sync.State);

        var question = TestGame.Message("question.asked");
        await seeker.Sync.PublishAsync(question);
        Assert.Contains(seeker.Received, m => m.Id == question.Id); // local echo works offline
        Assert.Single(await seeker.Store.GetOutboxAsync(seekerTicket.SessionId));

        // Network is back (same device database, working connection).
        await seeker.Sync.DisconnectAsync();
        var online = new SignalRGameSyncService(seeker.Store, _server.ConnectionOptions());
        await online.ConnectAsync(seekerTicket.SessionId, TestGame.Seeker);

        await hider.WaitForAsync(m => m.Id == question.Id, "queued question on hider");
        await Eventually.Async(() => seeker.Store.GetOutboxAsync(seekerTicket.SessionId).Result.Count == 0, "empty outbox");
        await online.DisposeAsync();
    }

    [Fact]
    public async Task Latest_locations_are_delivered_after_connecting()
    {
        var (hiderTicket, seekerTicket) = await CreateGameAsync();
        var seeker = NewDevice();
        await seeker.JoinAsync(seekerTicket, "Bartek");
        await seeker.WaitUntilConnectedAsync();
        await seeker.Sync.PublishAsync(TestGame.Location(52.20, 21.00));
        var newest = TestGame.Location(52.21, 21.00);
        await seeker.Sync.PublishAsync(newest);
        await Eventually.Async(() => seeker.Store.GetOutboxAsync(seekerTicket.SessionId).Result.Count == 0, "sent");

        // The hider connects later (e.g. app restarted): it gets the seeker's newest position right away.
        var hider = NewDevice();
        await hider.JoinAsync(hiderTicket, "Anna");
        await hider.WaitForAsync(m => m.Id == newest.Id, "latest location");
        await Eventually.QuietPeriodAsync();

        // Catch-up delivers the history first; the latest positions come last, so a consumer that keeps the
        // last position per device ends up with the newest one.
        Assert.Equal(newest.Id, hider.Received.Last(m => m.Type == SyncMessageTypes.Location).Id);
    }

    [Fact]
    public async Task Peers_show_who_is_connected()
    {
        var (hiderTicket, seekerTicket) = await CreateGameAsync();
        var hider = NewDevice();
        var seeker = NewDevice();
        await hider.JoinAsync(hiderTicket, "Anna");
        await seeker.JoinAsync(seekerTicket, "Bartek");

        await Eventually.Async(() => hider.Sync.Peers.Count(p => p.IsConnected) == 2, "both connected");
        await seeker.Sync.DisconnectAsync();
        await Eventually.Async(
            () => hider.Sync.Peers.SingleOrDefault(p => p.DeviceId == seekerTicket.DeviceId) is { IsConnected: false },
            "seeker shown as disconnected");
    }

    [Fact]
    public async Task Finished_game_cannot_be_joined()
    {
        var (hiderTicket, _) = await CreateGameAsync();
        var hider = NewDevice();
        await hider.JoinAsync(hiderTicket, "Anna");
        await hider.WaitUntilConnectedAsync();
        await hider.Sync.PublishAsync(TestGame.Message(SyncMessageTypes.SessionFinished));

        var api = _server.CreateRestClient();
        await Eventually.Async(() => api.GetLobbyAsync(ServerFactory.Url, hiderTicket.Code).Result.IsFinished, "finished");
        var error = await Assert.ThrowsAsync<OnlineException>(
            () => api.JoinLobbyAsync(ServerFactory.Url, hiderTicket.Code, new JoinLobbyRequest(TestGame.Seeker, "Late")));
        Assert.Equal(OnlineErrorCodes.LobbyFinished, error.Code);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No network");
    }
}
