using TransitGames.Core.Geography;
using TransitGames.Geo.Overpass;

namespace TransitGames.Geo.Areas;

/// <summary>Game areas from OSM administrative boundaries (city, district...).</summary>
public sealed class BoundaryService
{
    private readonly IOverpassClient _overpass;

    public BoundaryService(IOverpassClient overpass) => _overpass = overpass;

    /// <summary>Boundaries containing the point, most local first (district before city before county).</summary>
    public async Task<IReadOnlyList<AdministrativeBoundary>> FindAtAsync(
        GeoPoint point,
        CancellationToken cancellationToken = default)
    {
        var response = await _overpass.QueryAsync(OverpassQueries.BoundariesAt(point), cancellationToken)
            .ConfigureAwait(false);
        return OverpassBoundaryParser.ParseList(response);
    }

    /// <summary>Downloads the boundary geometry, simplified to <see cref="GeoDefaults.BoundarySimplifyToleranceMeters"/>.</summary>
    public async Task<GameArea> DownloadAsync(AdministrativeBoundary boundary, CancellationToken cancellationToken = default)
    {
        var response = await _overpass.QueryAsync(OverpassQueries.RelationGeometry(boundary.RelationId), cancellationToken)
            .ConfigureAwait(false);
        var geometry = OverpassBoundaryParser.ParseGeometry(response, boundary.RelationId);
        return GameArea.Create(boundary.Name, geometry, GameAreaSource.OsmBoundary)
            .Simplify(GeoDefaults.BoundarySimplifyToleranceMeters);
    }
}
