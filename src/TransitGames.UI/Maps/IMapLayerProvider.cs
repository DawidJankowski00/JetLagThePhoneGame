using BruTile.Cache;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using NetTopologySuite.Geometries;
using TransitGames.Core.Geography;
using TransitGames.Core.Stations;
using TransitGames.Geo;
using Brush = Mapsui.Styles.Brush;
using Color = Mapsui.Styles.Color;
using Font = Mapsui.Styles.Font;

namespace TransitGames.UI.Maps;

/// <summary>
/// Shared map layers for all games. Layers are plain Mapsui <see cref="MemoryLayer"/>s; update them with the
/// <c>Set*</c> methods, which convert domain objects (WGS84) to map features.
/// </summary>
public interface IMapLayerProvider
{
    /// <summary>OpenStreetMap tiles with an identifying User-Agent and a persistent tile cache (OSM tile policy).</summary>
    ILayer CreateBaseLayer();

    /// <summary>Polygon layer with the given fill and outline colours (palette keys, see <see cref="MapPalette"/>).</summary>
    MemoryLayer CreatePolygonLayer(string name, string fillColorKey, string? outlineColorKey = null, double outlineWidth = 3);

    /// <summary>Round markers; features can carry a label (station names...).</summary>
    MemoryLayer CreatePointLayer(string name);

    void SetPolygon(MemoryLayer layer, Geometry? wgs84);

    void SetStations(MemoryLayer layer, IEnumerable<Station> stations);

    void SetPoints(MemoryLayer layer, IEnumerable<GeoPoint> points, string colorKey, double scale = 0.45);
}

public sealed class MapLayerProvider : IMapLayerProvider
{
    /// <summary>Resolution (map units per pixel) below which station names are drawn (≈ zoom 14).</summary>
    private const double LabelMaxResolution = 10;

    private static readonly TimeSpan TileCacheLifetime = TimeSpan.FromDays(14);

    private readonly string _tileCacheDirectory;

    public MapLayerProvider(string tileCacheDirectory) => _tileCacheDirectory = tileCacheDirectory;

    public ILayer CreateBaseLayer()
    {
        var source = new HttpTileSource(
            new GlobalSphericalMercator(),
            "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
            name: "OpenStreetMap",
            persistentCache: new FileCache(_tileCacheDirectory, "png", TileCacheLifetime),
            attribution: new BruTile.Attribution("© OpenStreetMap contributors", "https://www.openstreetmap.org/copyright"),
            configureHttpRequestMessage: r => r.Headers.TryAddWithoutValidation("User-Agent", GeoDefaults.UserAgent));
        return new TileLayer(source) { Name = "osm" };
    }

    public MemoryLayer CreatePolygonLayer(string name, string fillColorKey, string? outlineColorKey = null, double outlineWidth = 3) =>
        new(name)
        {
            Style = new VectorStyle
            {
                Fill = new Brush(MapPalette.Get(fillColorKey)),
                Outline = outlineColorKey is null ? null : new Pen(MapPalette.Get(outlineColorKey), outlineWidth),
                Line = outlineColorKey is null ? null : new Pen(MapPalette.Get(outlineColorKey), outlineWidth),
            },
        };

    public MemoryLayer CreatePointLayer(string name) => new(name) { Style = null };

    public void SetPolygon(MemoryLayer layer, Geometry? wgs84)
    {
        layer.Features = wgs84 is null || wgs84.IsEmpty ? [] : [new GeometryFeature(MapCoordinates.ToWorld(wgs84))];
        layer.DataHasChanged();
    }

    public void SetStations(MemoryLayer layer, IEnumerable<Station> stations)
    {
        var enabled = Marker(MapPalette.Get(MapPalette.Station), 0.45);
        var disabled = Marker(MapPalette.Get(MapPalette.StationExcluded), 0.35);
        layer.Features = stations
            .OrderBy(s => s.IsEnabled) // enabled drawn on top
            .Select(s =>
            {
                var feature = new PointFeature(MapCoordinates.ToWorld(s.Location)) { Data = s };
                feature.Styles.Add(s.IsEnabled ? enabled : disabled);
                feature.Styles.Add(Label(s.Name, s.IsEnabled));
                return feature;
            })
            .ToList();
        layer.DataHasChanged();
    }

    public void SetPoints(MemoryLayer layer, IEnumerable<GeoPoint> points, string colorKey, double scale = 0.45)
    {
        var style = Marker(MapPalette.Get(colorKey), scale);
        layer.Features = points.Select(p =>
        {
            var feature = new PointFeature(MapCoordinates.ToWorld(p));
            feature.Styles.Add(style);
            return feature;
        }).ToList();
        layer.DataHasChanged();
    }

    private static SymbolStyle Marker(Color color, double scale) => new()
    {
        SymbolType = SymbolType.Ellipse,
        Fill = new Brush(color),
        Outline = new Pen(Color.White, 2),
        SymbolScale = scale,
    };

    private static LabelStyle Label(string text, bool enabled) => new()
    {
        Text = text,
        MaxVisible = LabelMaxResolution,
        Font = new Font { Size = 12 },
        ForeColor = enabled ? Color.Black : Color.Gray,
        BackColor = new Brush(new Color(255, 255, 255, 200)),
        Offset = new Offset(0, -18),
        CollisionDetection = true,
    };
}
