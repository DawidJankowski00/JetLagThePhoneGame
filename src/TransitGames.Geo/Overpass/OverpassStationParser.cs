using System.Text.RegularExpressions;
using TransitGames.Core.Geography;
using TransitGames.Core.Stations;

namespace TransitGames.Geo.Overpass;

/// <summary>Turns Overpass elements into <see cref="Station"/>s and classifies their transit modes from OSM tags.</summary>
public static partial class OverpassStationParser
{
    /// <summary>
    /// Stations matching <paramref name="requested"/> (their <see cref="Station.Modes"/> are limited to it).
    /// Elements without a name or position are skipped: players must be able to name a station.
    /// </summary>
    public static IReadOnlyList<Station> Parse(OverpassResponse response, TransitModes requested)
    {
        var result = new List<Station>();
        foreach (var element in response.Elements)
        {
            var modes = Classify(element) & requested;
            var name = StationName(element);
            var location = element.Lat is { } lat && element.Lon is { } lon
                ? new GeoPoint(lat, lon)
                : element.Center is { } c ? new GeoPoint(c.Lat, c.Lon) : (GeoPoint?)null;

            if (modes == TransitModes.None || string.IsNullOrEmpty(name) || location is null)
            {
                continue;
            }

            result.Add(new Station
            {
                Id = $"osm:{element.Type}/{element.Id}",
                Name = name,
                Location = location.Value,
                Modes = modes,
            });
        }

        return result;
    }

    /// <summary>
    /// Name of the station the element belongs to. Stop positions/platforms are often named after the stop plus
    /// a platform number that repeats the end of their <c>ref</c>/<c>local_ref</c> (Warsaw: "Centrum 07",
    /// ref 701307). The number is dropped so all platforms of one stop share a name and get merged.
    /// </summary>
    public static string? StationName(OverpassElement element)
    {
        var name = element.Tag("name")?.Trim();
        if (string.IsNullOrEmpty(name) || element.Tag("railway") is "station" or "halt" || element.Tag("public_transport") is "station")
        {
            return name;
        }

        var match = PlatformSuffix().Match(name);
        var reference = element.Tag("local_ref") ?? element.Tag("ref");
        return match.Success && reference is not null && reference.EndsWith(match.Groups[1].Value, StringComparison.Ordinal)
            ? name[..match.Index].TrimEnd()
            : name;
    }

    public static TransitModes Classify(OverpassElement element)
    {
        var railway = element.Tag("railway");
        var station = element.Tag("station");
        var publicTransport = element.Tag("public_transport");
        var modes = TransitModes.None;

        if (station is "miniature" || element.Tag("usage") is "tourism")
        {
            return TransitModes.None;
        }

        if (railway is "station" or "halt")
        {
            modes |= station switch
            {
                "subway" => TransitModes.Subway,
                "tram" => TransitModes.Tram,
                _ => TransitModes.Rail,
            };
        }

        if (railway is "tram_stop")
        {
            modes |= TransitModes.Tram;
        }

        if (element.Tag("highway") is "bus_stop" || element.Tag("amenity") is "bus_station")
        {
            modes |= TransitModes.Bus;
        }

        if (railway is "station" or "halt" || publicTransport is "station")
        {
            if (IsYes(element, "subway"))
            {
                modes |= TransitModes.Subway;
            }

            if (IsYes(element, "train") || IsYes(element, "light_rail"))
            {
                modes |= TransitModes.Rail;
            }

            if (IsYes(element, "tram"))
            {
                modes |= TransitModes.Tram;
            }

            if (IsYes(element, "bus"))
            {
                modes |= TransitModes.Bus;
            }
        }

        return modes;
    }

    private static bool IsYes(OverpassElement element, string key) => element.Tag(key) is "yes";

    [GeneratedRegex(@"\s(\d{1,2})$")]
    private static partial Regex PlatformSuffix();
}
