using System.Globalization;
using System.Text;
using System.Text.Json;
using NetTopologySuite.Simplify;
using SQLite;
using TransitGames.Core.Games;
using TransitGames.Core.Geography;
using TransitGames.Core.Persistence;
using TransitGames.Core.Stations;
using TransitGames.Geo.Areas;
using TransitGames.Geo.Overpass;

namespace TransitGames.Geo.Places;

/// <summary>
/// Places of a category around a game area: the game's own stations, or points/districts downloaded from
/// OpenStreetMap once and cached (offline afterwards). The download reaches beyond the area, because the nearest
/// museum of someone at the edge of the area may lie outside it.
/// </summary>
public sealed class PlaceCatalog
{
    /// <summary>How far beyond the area places are downloaded.</summary>
    public const double MarginMeters = 3000;

    /// <summary>District outlines are simplified to this tolerance to keep questions small.</summary>
    public const double DistrictToleranceMeters = 25;

    private static readonly int[] DistrictLevels = [9, 10, 8];

    private readonly AppDatabase _database;
    private readonly IOverpassClient _overpass;

    public PlaceCatalog(AppDatabase database, IOverpassClient overpass)
    {
        _database = database;
        _overpass = overpass;
    }

    public async Task<IReadOnlyList<Place>?> GetCachedAsync(GameArea area, string categoryId)
    {
        var db = await _database.GetAsync<PlaceSetRow>().ConfigureAwait(false);
        var row = await db.FindAsync<PlaceSetRow>(Key(area, categoryId)).ConfigureAwait(false);
        return row is null ? null : JsonSerializer.Deserialize<List<Place>>(row.Json, GameJson.Options);
    }

    /// <summary>
    /// Cached places, downloading them first if needed (or when <paramref name="refresh"/>). Places without a
    /// name in OSM are called "<paramref name="unnamedLabel"/> (id)".
    /// </summary>
    /// <exception cref="OverpassException">No Overpass server answered.</exception>
    public async Task<IReadOnlyList<Place>> GetAsync(
        GameArea area,
        IReadOnlyList<Station> stations,
        string categoryId,
        string unnamedLabel,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        var category = PlaceCategories.Get(categoryId);
        if (category.IsGameStations)
        {
            return stations.Where(s => s.IsEnabled).Select(s => new Place(s.Id, s.Name, s.Location)).ToList();
        }

        if (!refresh && await GetCachedAsync(area, categoryId).ConfigureAwait(false) is { } cached)
        {
            return cached;
        }

        var places = category.Shape == PlaceShape.Area
            ? await DownloadDistrictsAsync(area, cancellationToken).ConfigureAwait(false)
            : await DownloadPointsAsync(area, category, unnamedLabel, cancellationToken).ConfigureAwait(false);

        var db = await _database.GetAsync<PlaceSetRow>().ConfigureAwait(false);
        await db.InsertOrReplaceAsync(new PlaceSetRow
        {
            Key = Key(area, categoryId),
            Json = JsonSerializer.Serialize(places, GameJson.Options),
            SavedAtTicks = DateTimeOffset.UtcNow.UtcTicks,
        }).ConfigureAwait(false);
        return places;
    }

    private async Task<List<Place>> DownloadPointsAsync(GameArea area, PlaceCategory category, string unnamedLabel, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"[out:json][timeout:60][bbox:{BoundingBox(area)}];\n(\n");
        foreach (var selector in category.OverpassSelectors)
        {
            sb.Append("  ").Append(selector).Append(";\n");
        }

        sb.Append(");\nout center tags;");
        var response = await _overpass.QueryAsync(sb.ToString(), cancellationToken).ConfigureAwait(false);
        return ParsePoints(response, unnamedLabel);
    }

    internal static List<Place> ParsePoints(OverpassResponse response, string fallbackName)
    {
        var places = new List<Place>();
        foreach (var element in response.Elements)
        {
            var (lat, lon) = element.Lat is { } la && element.Lon is { } lo ? (la, lo)
                : element.Center is { } c ? (c.Lat, c.Lon)
                : (double.NaN, double.NaN);
            if (double.IsNaN(lat))
            {
                continue;
            }

            var name = element.Tag("name") ?? $"{fallbackName} ({element.Type[0]}{element.Id})";
            places.Add(new Place($"{element.Type}/{element.Id}", name, new GeoPoint(lat, lon)));
        }

        return places.DistinctBy(p => p.Id).OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList();
    }

    private async Task<List<Place>> DownloadDistrictsAsync(GameArea area, CancellationToken cancellationToken)
    {
        foreach (var level in DistrictLevels)
        {
            var query = string.Create(CultureInfo.InvariantCulture,
                $"[out:json][timeout:90][bbox:{BoundingBox(area)}];\nrel[\"boundary\"=\"administrative\"][\"admin_level\"=\"{level}\"][\"name\"];\nout geom;");
            var response = await _overpass.QueryAsync(query, cancellationToken).ConfigureAwait(false);
            var districts = ParseDistricts(response, area);
            if (districts.Count >= 2)
            {
                return districts;
            }
        }

        return [];
    }

    internal static List<Place> ParseDistricts(OverpassResponse response, GameArea area)
    {
        var districts = new List<Place>();
        foreach (var relation in response.Elements.Where(e => e.Type == "relation"))
        {
            NetTopologySuite.Geometries.Geometry outline;
            try
            {
                outline = OverpassBoundaryParser.ParseGeometry(response, relation.Id);
            }
            catch (OverpassException)
            {
                continue; // incomplete relation at the edge of the bounding box
            }

            var local = area.Projection.Project(outline);
            if (!local.Intersects(area.LocalGeometry))
            {
                continue;
            }

            var simplified = area.Projection.Unproject(TopologyPreservingSimplifier.Simplify(local, DistrictToleranceMeters));
            var inside = simplified.InteriorPoint;
            districts.Add(new Place(
                $"relation/{relation.Id}",
                relation.Tag("name")!,
                new GeoPoint(inside.Y, inside.X),
                GeoJson.Write(simplified)));
        }

        return districts.OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList();
    }

    private static string BoundingBox(GameArea area)
    {
        var (southWest, northEast) = area.Bounds;
        var dLat = MarginMeters / 111_320.0;
        var dLon = MarginMeters / (111_320.0 * Math.Cos(area.Center.Latitude * Math.PI / 180));
        return string.Create(CultureInfo.InvariantCulture,
            $"{southWest.Latitude - dLat:F6},{southWest.Longitude - dLon:F6},{northEast.Latitude + dLat:F6},{northEast.Longitude + dLon:F6}");
    }

    private static string Key(GameArea area, string categoryId) => $"{area.Key}|{categoryId}";

    [Table("places")]
    internal sealed class PlaceSetRow
    {
        [PrimaryKey]
        public string Key { get; set; } = string.Empty;

        public string Json { get; set; } = string.Empty;

        public long SavedAtTicks { get; set; }
    }
}
