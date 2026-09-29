using System.Globalization;
using System.Text;
using TransitGames.Core.Geography;
using TransitGames.Core.Stations;

namespace TransitGames.Geo.Overpass;

/// <summary>Overpass QL builders. Results are filtered to the exact area polygon locally.</summary>
public static class OverpassQueries
{
    /// <summary>Stations/stops of the requested modes in a bounding box, ways and relations as centre points.</summary>
    public static string Stations(GeoPoint southWest, GeoPoint northEast, TransitModes modes)
    {
        if (modes == TransitModes.None)
        {
            throw new ArgumentException("Select at least one transit mode.", nameof(modes));
        }

        var bbox = string.Create(CultureInfo.InvariantCulture,
            $"{southWest.Latitude:F6},{southWest.Longitude:F6},{northEast.Latitude:F6},{northEast.Longitude:F6}");
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"[out:json][timeout:60][bbox:{bbox}];\n(\n");
        if ((modes & (TransitModes.Rail | TransitModes.Subway)) != 0)
        {
            sb.Append("  nwr[\"railway\"~\"^(station|halt)$\"];\n");
            sb.Append("  nwr[\"public_transport\"=\"station\"][\"train\"=\"yes\"];\n");
            sb.Append("  nwr[\"public_transport\"=\"station\"][\"subway\"=\"yes\"];\n");
        }

        if ((modes & TransitModes.Tram) != 0)
        {
            sb.Append("  nwr[\"railway\"=\"tram_stop\"];\n");
            sb.Append("  nwr[\"public_transport\"=\"station\"][\"tram\"=\"yes\"];\n");
        }

        if ((modes & TransitModes.Bus) != 0)
        {
            sb.Append("  nwr[\"highway\"=\"bus_stop\"];\n");
            sb.Append("  nwr[\"amenity\"=\"bus_station\"];\n");
        }

        sb.Append(");\nout center tags;");
        return sb.ToString();
    }

    /// <summary>Administrative boundaries (relations) containing a point, tags only.</summary>
    public static string BoundariesAt(GeoPoint point) => string.Create(CultureInfo.InvariantCulture,
        $"[out:json][timeout:30];\nis_in({point.Latitude:F6},{point.Longitude:F6})->.a;\nrel(pivot.a)[\"boundary\"=\"administrative\"][\"admin_level\"];\nout tags;");

    /// <summary>Full geometry of one relation.</summary>
    public static string RelationGeometry(long relationId) => string.Create(CultureInfo.InvariantCulture,
        $"[out:json][timeout:90];\nrel({relationId});\nout geom;");
}
