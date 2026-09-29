using Mapsui;
using Mapsui.Projections;
using NetTopologySuite.Geometries;
using TransitGames.Core.Geography;

namespace TransitGames.UI.Maps;

/// <summary>Conversions between WGS84 (domain) and Spherical Mercator (Mapsui world coordinates). Display only.</summary>
public static class MapCoordinates
{
    public static MPoint ToWorld(GeoPoint point)
    {
        var (x, y) = SphericalMercator.FromLonLat(point.Longitude, point.Latitude);
        return new MPoint(x, y);
    }

    public static GeoPoint ToGeo(MPoint world)
    {
        var (lon, lat) = SphericalMercator.ToLonLat(world.X, world.Y);
        return new GeoPoint(lat, lon);
    }

    /// <summary>Copies a WGS84 geometry (X = lon, Y = lat) into Spherical Mercator.</summary>
    public static Geometry ToWorld(Geometry wgs84)
    {
        var copy = wgs84.Copy();
        copy.Apply(new MercatorFilter());
        copy.GeometryChanged();
        return copy;
    }

    public static MRect ToWorld(Envelope wgs84)
    {
        var (minX, minY) = SphericalMercator.FromLonLat(wgs84.MinX, wgs84.MinY);
        var (maxX, maxY) = SphericalMercator.FromLonLat(wgs84.MaxX, wgs84.MaxY);
        return new MRect(minX, minY, maxX, maxY);
    }

    private sealed class MercatorFilter : IEntireCoordinateSequenceFilter
    {
        public bool Done => false;

        public bool GeometryChanged => true;

        public void Filter(CoordinateSequence seq)
        {
            for (var i = 0; i < seq.Count; i++)
            {
                var (x, y) = SphericalMercator.FromLonLat(seq.GetX(i), seq.GetY(i));
                seq.SetX(i, x);
                seq.SetY(i, y);
            }
        }
    }
}
