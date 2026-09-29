using System.Security.Cryptography;
using System.Text;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using TransitGames.Core.Geography;
using TransitGames.Core.Stations;
using TransitGames.Geo.Areas;

namespace TransitGames.Geo.Stations;

public sealed record StationImportResult(IReadOnlyList<Station> Stations, int SkippedOutsideArea, int SkippedWithoutName);

/// <summary>
/// Imports custom stations from GeoJSON: every Point feature with a "name" property becomes a custom station.
/// Ids are derived from name + position, so importing the same file twice updates instead of duplicating.
/// </summary>
public static class GeoJsonStationImporter
{
    private static readonly string[] NameProperties = ["name", "Name", "NAME", "title"];

    public static StationImportResult Import(string geoJson, GameArea area)
    {
        var stations = new List<Station>();
        int outside = 0, unnamed = 0;
        foreach (var feature in GeoJson.ReadFeatures(geoJson))
        {
            foreach (var point in Points(feature.Geometry))
            {
                var location = new GeoPoint(point.Y, point.X);
                var name = ReadName(feature.Attributes);
                if (name is null)
                {
                    unnamed++;
                }
                else if (!area.Contains(location))
                {
                    outside++;
                }
                else
                {
                    stations.Add(new Station
                    {
                        Id = CustomId(name, location),
                        Name = name,
                        Location = location,
                        Modes = TransitModes.None,
                        IsCustom = true,
                    });
                }
            }
        }

        return new StationImportResult(stations.DistinctBy(s => s.Id).ToList(), outside, unnamed);
    }

    public static string CustomId(string name, GeoPoint location)
    {
        var text = FormattableString.Invariant($"{name}|{location.Latitude:F6}|{location.Longitude:F6}");
        return "custom:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 8);
    }

    private static IEnumerable<Point> Points(Geometry? geometry) => geometry switch
    {
        Point p when !p.IsEmpty => [p],
        MultiPoint mp => mp.Geometries.OfType<Point>(),
        _ => [],
    };

    private static string? ReadName(IAttributesTable? attributes)
    {
        if (attributes is null)
        {
            return null;
        }

        foreach (var key in NameProperties)
        {
            if (attributes.Exists(key) && attributes[key]?.ToString()?.Trim() is { Length: > 0 } name)
            {
                return name;
            }
        }

        return null;
    }
}
