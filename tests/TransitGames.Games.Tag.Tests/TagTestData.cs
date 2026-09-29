using System.Text.Json;
using NetTopologySuite.Geometries;
using TransitGames.Core.Games;
using TransitGames.Core.Geography;
using TransitGames.Core.Sync;
using TransitGames.Games.Tag.Sync;
using TransitGames.Geo.Projection;

namespace TransitGames.Games.Tag.Tests;

internal static class TagTestData
{
    public static readonly DateTimeOffset Start = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    public static readonly GeoPoint Center = new(52.2297, 21.0122);
    public static readonly LocalProjection Projection = LocalProjection.CenteredOn(Center);

    /// <summary>A small deck whose cards the tests draw by id.</summary>
    public const string DeckJson = """
        { "cards": [
          { "id": "ch-timed", "type": "challenge", "reward": 5, "timeLimitMinutes": 10, "title": "Timed", "description": "Do it fast." },
          { "id": "ch-free", "type": "challenge", "reward": 3, "title": "Free", "description": "Any time." },
          { "id": "cu-turn", "type": "curse", "reward": 4, "duration": "untilEndOfTurn",
            "restriction": { "kind": "onlyTransport", "modes": ["bus", "tram"] }, "title": "Surface", "description": "Buses and trams only." },
          { "id": "cu-game", "type": "curse", "reward": 6, "duration": "wholeGame",
            "restriction": { "kind": "banTransport", "modes": ["train"] }, "title": "No trains", "description": "No trains." },
          { "id": "cu-double", "type": "curse", "reward": 5, "duration": "minutes:45",
            "restriction": { "kind": "fareMultiplier", "factor": 2 }, "title": "Double", "description": "Fares x2." },
          { "id": "cu-nodraw", "type": "curse", "reward": 2, "duration": "minutes:20", "restriction": { "kind": "noDraw" },
            "title": "Locked", "description": "No drawing." },
          { "id": "cu-honour", "type": "curse", "reward": 1, "title": "Backwards", "description": "Sit backwards." }
        ] }
        """;

    public static GeoPoint At(double east, double north) => Projection.Unproject(new Coordinate(east, north));

    public static TagSettings Settings(Action<TagSettings>? change = null)
    {
        var settings = new TagSettings
        {
            GameDuration = TimeSpan.FromHours(2),
            HeadStart = TimeSpan.FromMinutes(30),
            StartingCoins = 20,
            TurnBonus = 10,
            CardsJson = DeckJson,
            StartedAt = Start.AddMinutes(-10), // the lobby opened before the start
        };
        change?.Invoke(settings);
        return settings;
    }

    /// <summary>What the app does with a message: JSON round trip, the game's handler, then the role change it carries.</summary>
    public static void Deliver(TagSession session, SyncMessage message)
    {
        var json = JsonSerializer.Deserialize<SyncMessage>(JsonSerializer.Serialize(message, GameJson.Options), GameJson.Options)!;
        new TagMessageHandler().Handle(session, json);
        if (json.RoleChange is { } change)
        {
            session.ApplyRoleChange(change, json.Timestamp);
        }
    }
}

/// <summary>An online game on several phones: device 0 is the host; every message reaches every device.</summary>
internal sealed class TagTable
{
    public static readonly DateTimeOffset Start = TagTestData.Start;

    public TagTable(int players = 3, Action<TagSettings>? change = null)
    {
        IGameModule module = new TagModule();
        var settings = TagTestData.Settings(change);
        var json = module.SerializeSettings(settings);
        var sessionId = Guid.NewGuid();
        Ids = [.. Enumerable.Range(1, players).Select(i => new Guid(i, 0, 0, new byte[8]))];
        Devices = [.. Ids.Select((id, i) =>
        {
            var session = (TagSession)module.CreateSession(module.DeserializeSettings(json), TagRoles.Chaser, sessionId);
            session.InitializeDevice(id, isHost: i == 0);
            session.Start(settings.StartedAt!.Value);
            return session;
        })];
    }

    public IReadOnlyList<Guid> Ids { get; }

    public IReadOnlyList<TagSession> Devices { get; }

    public TagSession Host => Devices[0];

    public TagSession Of(Guid playerId) => Devices[Ids.ToList().IndexOf(playerId)];

    public TagSession Runner => Devices.Single(d => d.IsLocalRunner);

    public IEnumerable<TagSession> Chasers => Devices.Where(d => !d.IsLocalRunner);

    public string Name(Guid id) => "P" + (Ids.ToList().IndexOf(id) + 1);

    /// <summary>Destination of player i: i + 2 km east of the centre.</summary>
    public GeoPoint DestinationOf(int index) => TagTestData.At(2000 + index * 1000, 0);

    public void Send(SyncMessage message)
    {
        foreach (var device in Devices)
        {
            TagTestData.Deliver(device, message);
        }
    }

    /// <summary>Every player picks a destination, the host starts; <paramref name="runner"/> overrides the draw.</summary>
    public void StartGame(Guid? runner = null, int seed = 1, Func<int, GeoPoint>? destinations = null)
    {
        for (var i = 0; i < Devices.Count; i++)
        {
            var device = Devices[i];
            var destination = destinations?.Invoke(i) ?? DestinationOf(i);
            Send(TagMessages.SetDestination(device, new DestinationPayload(Ids[i], destination, "D" + (i + 1)), Start.AddMinutes(-5)));
        }

        var start = Host.CreateStart(Start, TagTestData.Center, [.. Ids.Select(id => (id, Name(id)))], new Random(seed))
            ?? throw new InvalidOperationException("The game could not start.");
        Send(TagMessages.StartGame(Host, runner is { } id ? start with { RunnerId = id } : start));
    }

    public void Catch(DateTimeOffset at, Guid? catcher, GeoPoint? point = null, int seed = 1)
    {
        var runner = Runner;
        var payload = runner.CreateCatch(at, catcher, point ?? TagTestData.At(100, 100), new Random(seed));
        Send(TagMessages.ConfirmCatch(runner, payload, runner.LocalPlayerId));
    }

    public void Draw(string cardId, DateTimeOffset at)
    {
        var runner = Runner;
        Send(TagMessages.DrawCard(runner, new CardDrawPayload(runner.CurrentTurn!.Index, Guid.NewGuid(), cardId, at)));
    }
}
