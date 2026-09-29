using System.Text.Json;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.IO.Converters;
using TransitGames.Geo.Projection;

namespace TransitGames.Geo.Areas;

/// <summary>Reads and writes GeoJSON (RFC 7946, WGS84 lon/lat) with NetTopologySuite.</summary>
public static class GeoJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>
    /// Reads a GeoJSON geometry, Feature or FeatureCollection and returns the union of all its polygons
    /// as a valid Polygon or MultiPolygon.
    /// </summary>
    /// <exception cref="GeoJsonFormatException">The text is not GeoJSON or contains no polygon.</exception>
    public static Geometry ReadPolygonal(string geoJson)
    {
        var polygons = ReadGeometries(geoJson)
            .SelectMany(Flatten)
            .OfType<Polygon>()
            .Where(p => !p.IsEmpty)
            .Select(p => GeometryFixer.Fix(p))
            .ToList();

        if (polygons.Count == 0)
        {
            throw new GeoJsonFormatException("The GeoJSON does not contain any polygon.");
        }

        var union = GeometryFactories.Wgs84.BuildGeometry(polygons).Union();
        return union is Polygon or MultiPolygon
            ? union
            : GeometryFactories.Wgs84.BuildGeometry(Flatten(union).OfType<Polygon>().ToList());
    }

    /// <summary>Reads every feature of a GeoJSON text (a bare geometry becomes a feature without attributes).</summary>
    public static IReadOnlyList<IFeature> ReadFeatures(string geoJson)
    {
        using var document = Parse(geoJson);
        var type = document.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
        try
        {
            return type switch
            {
                "FeatureCollection" => [.. Deserialize<FeatureCollection>(geoJson)],
                "Feature" => [Deserialize<IFeature>(geoJson)],
                _ => [new Feature(Deserialize<Geometry>(geoJson), new AttributesTable())],
            };
        }
        catch (JsonException ex)
        {
            throw new GeoJsonFormatException("The GeoJSON is malformed: " + ex.Message, ex);
        }
    }

    public static string Write(Geometry geometry) => JsonSerializer.Serialize(geometry, Options);

    public static string Write(FeatureCollection features) => JsonSerializer.Serialize(features, Options);

    private static IEnumerable<Geometry> ReadGeometries(string geoJson) =>
        ReadFeatures(geoJson).Select(f => f.Geometry).Where(g => g is not null);

    private static IEnumerable<Geometry> Flatten(Geometry geometry)
    {
        if (geometry is GeometryCollection collection)
        {
            for (var i = 0; i < collection.NumGeometries; i++)
            {
                foreach (var part in Flatten(collection.GetGeometryN(i)))
                {
                    yield return part;
                }
            }
        }
        else
        {
            yield return geometry;
        }
    }

    private static T Deserialize<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new GeoJsonFormatException("The GeoJSON is empty.");

    private static JsonDocument Parse(string geoJson)
    {
        if (string.IsNullOrWhiteSpace(geoJson))
        {
            throw new GeoJsonFormatException("The GeoJSON is empty.");
        }

        try
        {
            return JsonDocument.Parse(geoJson, new JsonDocumentOptions { AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new GeoJsonFormatException("The file is not valid JSON: " + ex.Message, ex);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { AllowTrailingCommas = true };
        options.Converters.Add(new GeoJsonConverterFactory(GeometryFactories.Wgs84));
        return options;
    }
}

public sealed class GeoJsonFormatException : FormatException
{
    public GeoJsonFormatException(string message)
        : base(message)
    {
    }

    public GeoJsonFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
