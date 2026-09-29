using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using NetTopologySuite.Geometries;
using TransitGames.Core.Events;
using TransitGames.Core.Persistence;
using TransitGames.Geo.Areas;
using TransitGames.Tracking.Export;
using TransitGames.Tracking.Storage;
using static TransitGames.Tracking.Tests.TestData;

namespace TransitGames.Tracking.Tests;

public class ExportTests
{
    private static readonly XNamespace Gpx = GpxWriter.Namespace;

    private static readonly Guid SessionId = Guid.NewGuid();

    private static readonly TrackWithPoints TwoSegments = new(
        Track(SessionId) with { DistanceMeters = 150 },
        [
            Point(0, 0, 0, altitude: 110.24),
            Point(0, 100, 10, altitude: 111),
            Point(1, 5000, 400),
            Point(1, 5050, 410),
        ]);

    private static readonly GameEvent[] Events =
    [
        GameEvent.Create(SessionId, T0.AddSeconds(5), GameEventTypes.QuestionAsked, "Pytanie: radar 1 km",
            details: "Czy jesteś w promieniu 1 km?", location: North(50), actorRoleId: "chaser",
            data: new Dictionary<string, string> { ["category"] = "radar" }),
        GameEvent.Create(SessionId, T0.AddSeconds(405), GameEventTypes.Caught, "Złapany!", location: North(5020)),
        GameEvent.Create(SessionId, T0, GameEventTypes.SessionStarted, "Start"), // no location: not exported
    ];

    [Fact]
    public void Gpx_IsValidAgainstOfficialSchema()
    {
        var xml = WriteGpx(TwoSegments, Events);

        var schemas = new XmlSchemaSet();
        using (var xsd = typeof(ExportTests).Assembly.GetManifestResourceStream("gpx.xsd")!)
        {
            schemas.Add(GpxWriter.Namespace, XmlReader.Create(xsd));
        }

        var errors = new List<string>();
        XDocument.Parse(xml).Validate(schemas, (_, e) => errors.Add(e.Message));

        Assert.Empty(errors);
    }

    [Fact]
    public void Gpx_HasWaypointsForLocatedEvents_AndOneTrackSegmentPerRecordingSegment()
    {
        var doc = XDocument.Parse(WriteGpx(TwoSegments, Events));
        var root = doc.Root!;

        Assert.Equal("1.1", root.Attribute("version")!.Value);
        var waypoints = root.Elements(Gpx + "wpt").ToList();
        Assert.Equal(["Pytanie: radar 1 km", "Złapany!"], waypoints.Select(w => w.Element(Gpx + "name")!.Value));
        Assert.Equal(GameEventTypes.QuestionAsked, waypoints[0].Element(Gpx + "type")!.Value);
        Assert.Equal("2026-09-01T12:00:05Z", waypoints[0].Element(Gpx + "time")!.Value);

        var track = Assert.Single(root.Elements(Gpx + "trk"));
        Assert.Equal("Tag · Goniący", track.Element(Gpx + "name")!.Value);
        Assert.Equal("Pixel 7", track.Element(Gpx + "desc")!.Value);
        var segments = track.Elements(Gpx + "trkseg").ToList();
        Assert.Equal([2, 2], segments.Select(s => s.Elements(Gpx + "trkpt").Count()));

        var first = segments[0].Elements(Gpx + "trkpt").First();
        Assert.Equal("52.2319", first.Attribute("lat")!.Value);
        Assert.Equal("21.0067", first.Attribute("lon")!.Value);
        Assert.Equal("110.2", first.Element(Gpx + "ele")!.Value);
        Assert.Null(segments[1].Elements(Gpx + "trkpt").First().Element(Gpx + "ele"));
    }

    [Fact]
    public void Gpx_UsesInvariantNumbersRegardlessOfCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("pl-PL");
        try
        {
            Assert.DoesNotContain("52,23", WriteGpx(TwoSegments, Events));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void GeoJson_ReadsBackAsMultiLineStringAndEventPoints()
    {
        var json = WriteGeoJson(TwoSegments, Events);

        var features = GeoJson.ReadFeatures(json);

        Assert.Equal(3, features.Count);
        var line = Assert.IsType<MultiLineString>(features[0].Geometry);
        Assert.Equal(2, line.NumGeometries);
        Assert.Equal(21.0067, line.Coordinates[0].X, 7); // longitude first
        Assert.Equal(52.2319, line.Coordinates[0].Y, 7);
        Assert.Equal("track", features[0].Attributes["kind"]);
        Assert.Equal("Tag · Goniący", features[0].Attributes["name"]);

        var question = Assert.IsType<Point>(features[1].Geometry);
        Assert.Equal(North(50).Latitude, question.Y, 7);
        Assert.Equal(GameEventTypes.QuestionAsked, features[1].Attributes["type"]);
        Assert.Equal("Złapany!", features[2].Attributes["name"]);
    }

    [Fact]
    public void GeoJson_TimesMatchCoordinatesPerSegment()
    {
        using var doc = JsonDocument.Parse(WriteGeoJson(TwoSegments, Events));
        var track = doc.RootElement.GetProperty("features")[0];

        var times = track.GetProperty("properties").GetProperty("coordinateProperties").GetProperty("times");
        var coordinates = track.GetProperty("geometry").GetProperty("coordinates");
        Assert.Equal(coordinates.GetArrayLength(), times.GetArrayLength());
        Assert.Equal(coordinates[1].GetArrayLength(), times[1].GetArrayLength());
        Assert.Equal("2026-09-01T12:06:40Z", times[1][0].GetString());
        Assert.Equal(150, track.GetProperty("properties").GetProperty("distanceMeters").GetDouble());
    }

    [Fact]
    public void GeoJson_SingleSegmentIsLineString_AndSingleFixSegmentsAreDropped()
    {
        var track = TwoSegments with { Points = [.. TwoSegments.Points.Take(3)] };

        var features = GeoJson.ReadFeatures(WriteGeoJson(track, []));

        Assert.IsType<LineString>(Assert.Single(features).Geometry);
    }

    [Fact]
    public async Task Exporter_WritesStoredTrackWithSessionEvents()
    {
        using var temp = new TempDatabase();
        await using var database = new AppDatabase(new DatabaseOptions(temp.Path));
        var store = new SqliteTrackStore(database);
        var events = new FakeEventLog();
        foreach (var e in Events)
        {
            await events.AppendAsync(e);
        }

        await events.AppendAsync(GameEvent.Create(Guid.NewGuid(), T0, GameEventTypes.Caught, "Other game", location: North(1)));
        var track = await store.CreateAsync(TwoSegments.Track);
        foreach (var p in TwoSegments.Points)
        {
            await store.AppendAsync(track.Id, p);
        }

        var exporter = new TrackExporter(store, events, new FakeClock());
        var directory = Path.Combine(Path.GetTempPath(), $"transitgames-export-{Guid.NewGuid():N}");
        try
        {
            var path = await exporter.ExportToFileAsync(track.Id, TrackFileFormat.Gpx, directory);

            Assert.EndsWith(".gpx", path);
            Assert.StartsWith("transitgames-", Path.GetFileName(path));
            var doc = XDocument.Load(path);
            Assert.Equal(2, doc.Root!.Elements(Gpx + "wpt").Count());
            Assert.Equal(4, doc.Descendants(Gpx + "trkpt").Count());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string WriteGpx(TrackWithPoints track, IReadOnlyList<GameEvent> events)
    {
        using var stream = new MemoryStream();
        GpxWriter.Write(stream, track.Track.Name, [track], events, T0.AddHours(1));
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string WriteGeoJson(TrackWithPoints track, IReadOnlyList<GameEvent> events)
    {
        using var stream = new MemoryStream();
        GeoJsonTrackWriter.Write(stream, [track], events);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
