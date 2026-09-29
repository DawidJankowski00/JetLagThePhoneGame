using TransitGames.Core.Games;
using TransitGames.Core.Sync;

namespace TransitGames.Sync.Tests;

/// <summary>Runners and chasers swap during a game: routing and privacy must follow the new roles at once.</summary>
public sealed class RoleChangeTests : IAsyncLifetime
{
    private const string Runner = "runner";
    private const string Chaser = "chaser";

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

    private static CreateLobbyRequest TagRequest() => new(
        Guid.NewGuid(),
        "tag",
        "Tag",
        "{}",
        [new LobbyRole(Runner, "Runner"), new LobbyRole(Chaser, "Chaser")],
        [new LocationSharingRule(Chaser, Runner)], // chasers see runners, never the reverse
        Runner,
        "Anna");

    private static SyncMessage Caught(int fromVersion) =>
        TestGame.Message("tag.round.end") with { RoleChange = RoleChange.Swap(fromVersion, Runner, Chaser) };

    private async Task<(LobbyTicket Anna, LobbyTicket Bartek)> CreateGameAsync()
    {
        var api = _server.CreateRestClient();
        var anna = await api.CreateLobbyAsync(ServerFactory.Url, TagRequest());
        var bartek = await api.JoinLobbyAsync(ServerFactory.Url, anna.Code, new JoinLobbyRequest(Chaser, "Bartek"));
        return (anna, bartek.Ticket);
    }

    private async Task<TestDevice> ConnectAsync(LobbyTicket ticket, string player)
    {
        var device = new TestDevice(_server.ConnectionOptions());
        _devices.Add(device);
        await device.JoinAsync(ticket, player);
        await device.WaitUntilConnectedAsync();
        return device;
    }

    [Fact]
    public async Task After_a_swap_locations_follow_the_new_roles()
    {
        var (annaTicket, bartekTicket) = await CreateGameAsync();
        var anna = await ConnectAsync(annaTicket, "Anna");
        var bartek = await ConnectAsync(bartekTicket, "Bartek");

        var runnerPing = TestGame.Location(52.1, 21.0);
        await anna.Sync.PublishAsync(runnerPing);
        await bartek.WaitForAsync(m => m.Id == runnerPing.Id, "runner ping on chaser");

        var caught = Caught(0);
        await bartek.Sync.PublishAsync(caught);
        await anna.WaitForAsync(m => m.Id == caught.Id, "catch on runner");

        // Both transports followed the change before delivering the message.
        Assert.Equal((Chaser, 1), (anna.Sync.Link!.RoleId, anna.Sync.Link.RolesVersion));
        Assert.Equal((Runner, 1), (bartek.Sync.Link!.RoleId, bartek.Sync.Link.RolesVersion));
        Assert.Equal((Chaser, 1), ((await anna.Store.GetLinkAsync(annaTicket.SessionId))!.RoleId, 1));

        var newRunnerPing = TestGame.Location(52.2, 21.1);
        var newChaserPosition = TestGame.Location(52.3, 21.2);
        await bartek.Sync.PublishAsync(newRunnerPing);
        await anna.Sync.PublishAsync(newChaserPosition);

        var received = await anna.WaitForAsync(m => m.Id == newRunnerPing.Id, "new runner ping on new chaser");
        Assert.Equal(Runner, received.SenderRoleId);
        await Eventually.QuietPeriodAsync();
        Assert.DoesNotContain(bartek.Received, m => m.Id == newChaserPosition.Id);

        // The player list shows the new roles too.
        await Eventually.Async(() => bartek.Sync.Peers.Any(p => p.Name == "Anna" && p.RoleId == Chaser), "peers with new roles");
    }

    [Fact]
    public async Task Catch_up_after_a_swap_never_reveals_what_the_old_role_could_not_see()
    {
        var (annaTicket, bartekTicket) = await CreateGameAsync();
        var bartek = await ConnectAsync(bartekTicket, "Bartek");

        // Sent while Bartek chases: only chasers may see these.
        var chaserPlan = TestGame.Message("tag.note", SyncAudience.OwnTeam);
        var chaserPosition = TestGame.Location(52.1, 21.0);
        await bartek.Sync.PublishAsync(chaserPlan);
        await bartek.Sync.PublishAsync(chaserPosition);
        var caught = Caught(0);
        await bartek.Sync.PublishAsync(caught);
        await Eventually.Async(() => bartek.Sync.Link!.RoleId == Runner, "swap applied");

        // Anna was offline the whole time and is a chaser now; she catches up from the beginning.
        var anna = await ConnectAsync(annaTicket, "Anna");
        await anna.WaitForAsync(m => m.Id == caught.Id, "catch on Anna");
        await Eventually.QuietPeriodAsync();

        Assert.DoesNotContain(anna.Received, m => m.Id == chaserPlan.Id);
        Assert.DoesNotContain(anna.Received, m => m.Id == chaserPosition.Id);
        Assert.Equal((Chaser, 1), (anna.Sync.Link!.RoleId, anna.Sync.Link.RolesVersion));
    }

    [Fact]
    public async Task Positions_from_before_the_swap_are_not_offered_as_latest()
    {
        var (annaTicket, bartekTicket) = await CreateGameAsync();
        var anna = await ConnectAsync(annaTicket, "Anna");
        var oldRunnerPing = TestGame.Location(52.1, 21.0);
        await anna.Sync.PublishAsync(oldRunnerPing);
        var caught = Caught(0);
        await anna.Sync.PublishAsync(caught);

        // Bartek (a runner now) opens the app for the first time after the swap.
        var bartek = await ConnectAsync(bartekTicket, "Bartek");
        await bartek.WaitForAsync(m => m.Id == caught.Id, "catch on Bartek");
        await Eventually.QuietPeriodAsync();

        // He saw the ping as a chaser in the catch-up history, but it is not a "current" position of anybody.
        Assert.Single(bartek.Received, m => m.Id == oldRunnerPing.Id);
    }

    [Fact]
    public async Task Two_devices_ending_the_same_round_swap_the_roles_once()
    {
        var (annaTicket, bartekTicket) = await CreateGameAsync();
        var anna = await ConnectAsync(annaTicket, "Anna");
        var bartek = await ConnectAsync(bartekTicket, "Bartek");

        // Both press "caught" before hearing from the other.
        await Task.WhenAll(anna.Sync.PublishAsync(Caught(0)), bartek.Sync.PublishAsync(Caught(0)));
        await Eventually.Async(() => anna.Received.Count(m => m.Type == "tag.round.end") == 2, "both catches on Anna");
        await Eventually.Async(() => bartek.Received.Count(m => m.Type == "tag.round.end") == 2, "both catches on Bartek");

        Assert.Equal((Chaser, 1), (anna.Sync.Link!.RoleId, anna.Sync.Link.RolesVersion));
        Assert.Equal((Runner, 1), (bartek.Sync.Link!.RoleId, bartek.Sync.Link.RolesVersion));
        var lobby = await _server.CreateRestClient().GetLobbyAsync(ServerFactory.Url, annaTicket.Code);
        Assert.Equal(Chaser, lobby.Players.Single(p => p.Name == "Anna").RoleId);
        Assert.Equal(Runner, lobby.Players.Single(p => p.Name == "Bartek").RoleId);
    }

    [Fact]
    public async Task A_player_joining_after_a_swap_gets_the_current_roles_version()
    {
        var (annaTicket, _) = await CreateGameAsync();
        var anna = await ConnectAsync(annaTicket, "Anna");
        await anna.Sync.PublishAsync(Caught(0));
        await Eventually.AsyncCheck(async () => (await _server.CreateRestClient().GetLobbyAsync(ServerFactory.Url, annaTicket.Code))
            .Players.Any(p => p.Name == "Anna" && p.RoleId == Chaser), "swap on the server");

        var joined = await _server.CreateRestClient().JoinLobbyAsync(ServerFactory.Url, annaTicket.Code, new JoinLobbyRequest(Chaser, "Celina"));

        Assert.Equal(1, joined.Ticket.RolesVersion);
    }

    [Fact]
    public async Task Invalid_role_changes_are_ignored()
    {
        var (annaTicket, _) = await CreateGameAsync();
        var anna = await ConnectAsync(annaTicket, "Anna");

        // Makes everybody a chaser: not a permutation of the roles.
        await anna.Sync.PublishAsync(TestGame.Message("tag.x") with
        {
            RoleChange = new RoleChange(0, new Dictionary<string, string> { [Runner] = Chaser }),
        });
        await Eventually.QuietPeriodAsync();

        var lobby = await _server.CreateRestClient().GetLobbyAsync(ServerFactory.Url, annaTicket.Code);
        Assert.Equal(Runner, lobby.Players.Single(p => p.Name == "Anna").RoleId);
    }
}
