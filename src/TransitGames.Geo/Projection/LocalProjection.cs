using NetTopologySuite.Geometries;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using TransitGames.Core.Geography;

namespace TransitGames.Geo.Projection;

/// <summary>
/// Local metric coordinate system centred on a game area. All geometry operations (buffers, distances,
/// Voronoi cells) run in these coordinates, so one unit is one metre.
/// </summary>
/// <remarks>
/// Implemented as a WGS84 Transverse Mercator with the origin at the area centre and scale factor 1.
/// ProjNet 2.1 has no azimuthal equidistant projection; within 50 km of the origin the TM scale error is
/// below 0.004 %, i.e. below 2 m on a 50 km distance, which is far below GPS accuracy (see docs/DECISIONS.md).
/// NTS coordinates: X = longitude / easting, Y = latitude / northing.
/// </remarks>
public sealed class LocalProjection
{
    private static readonly CoordinateSystemFactory SystemFactory = new();
    private static readonly CoordinateTransformationFactory TransformationFactory = new();

    private readonly MathTransform _toLocal;
    private readonly MathTransform _toWgs84;

    private LocalProjection(GeoPoint origin)
    {
        Origin = origin;
        var wgs84 = GeographicCoordinateSystem.WGS84;
        var projection = SystemFactory.CreateProjection("Local transverse mercator", "Transverse_Mercator",
        [
            new ProjectionParameter("latitude_of_origin", origin.Latitude),
            new ProjectionParameter("central_meridian", origin.Longitude),
            new ProjectionParameter("scale_factor", 1.0),
            new ProjectionParameter("false_easting", 0.0),
            new ProjectionParameter("false_northing", 0.0),
        ]);
        var local = SystemFactory.CreateProjectedCoordinateSystem("Local metric", wgs84, projection, LinearUnit.Metre,
            new AxisInfo("East", AxisOrientationEnum.East), new AxisInfo("North", AxisOrientationEnum.North));

        _toLocal = TransformationFactory.CreateFromCoordinateSystems(wgs84, local).MathTransform;
        _toWgs84 = TransformationFactory.CreateFromCoordinateSystems(local, wgs84).MathTransform;
    }

    public GeoPoint Origin { get; }

    public static LocalProjection CenteredOn(GeoPoint origin) => new(origin);

    /// <summary>Metric coordinates (X east, Y north, metres from <see cref="Origin"/>).</summary>
    public Coordinate Project(GeoPoint point)
    {
        var (x, y) = _toLocal.Transform(point.Longitude, point.Latitude);
        return new Coordinate(x, y);
    }

    public GeoPoint Unproject(Coordinate local)
    {
        var (lon, lat) = _toWgs84.Transform(local.X, local.Y);
        return new GeoPoint(lat, lon);
    }

    public Point ProjectPoint(GeoPoint point) => GeometryFactories.Local.CreatePoint(Project(point));

    /// <summary>Copies a WGS84 geometry (X = lon, Y = lat) into local metric coordinates.</summary>
    public Geometry Project(Geometry wgs84) => Transform(wgs84, _toLocal, GeometryFactories.Local);

    /// <summary>Copies a local metric geometry back to WGS84 (X = lon, Y = lat).</summary>
    public Geometry Unproject(Geometry local) => Transform(local, _toWgs84, GeometryFactories.Wgs84);

    /// <summary>Distance in metres between two WGS84 points.</summary>
    public double Distance(GeoPoint a, GeoPoint b) => Project(a).Distance(Project(b));

    private static Geometry Transform(Geometry source, MathTransform transform, GeometryFactory factory)
    {
        var copy = factory.CreateGeometry(source);
        copy.Apply(new TransformFilter(transform));
        copy.GeometryChanged();
        return copy;
    }

    private sealed class TransformFilter(MathTransform transform) : IEntireCoordinateSequenceFilter
    {
        public bool Done => false;

        public bool GeometryChanged => true;

        public void Filter(CoordinateSequence seq)
        {
            for (var i = 0; i < seq.Count; i++)
            {
                var (x, y) = transform.Transform(seq.GetX(i), seq.GetY(i));
                seq.SetX(i, x);
                seq.SetY(i, y);
            }
        }
    }
}
