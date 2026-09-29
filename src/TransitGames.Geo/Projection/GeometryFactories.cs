using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace TransitGames.Geo.Projection;

/// <summary>Geometry factories for the two coordinate spaces used in the app.</summary>
public static class GeometryFactories
{
    /// <summary>WGS84 lon/lat (SRID 4326).</summary>
    public static GeometryFactory Wgs84 { get; } = NtsGeometryServices.Instance.CreateGeometryFactory(4326);

    /// <summary>Local metric coordinates of a <see cref="LocalProjection"/> (no SRID, metres).</summary>
    public static GeometryFactory Local { get; } = NtsGeometryServices.Instance.CreateGeometryFactory(0);
}
