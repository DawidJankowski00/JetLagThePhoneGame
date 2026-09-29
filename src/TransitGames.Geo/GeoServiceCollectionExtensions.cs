using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TransitGames.Geo.Areas;
using TransitGames.Geo.Overpass;
using TransitGames.Geo.Places;
using TransitGames.Geo.Stations;

namespace TransitGames.Geo;

public static class GeoServiceCollectionExtensions
{
    /// <summary>Overpass client, station catalog, OSM boundaries and places (POI). Requires <c>AddTransitGamesCore</c>.</summary>
    public static IServiceCollection AddTransitGamesGeo(this IServiceCollection services, OverpassOptions? options = null)
    {
        options ??= new OverpassOptions();
        services.TryAddSingleton(options);

        // The per-request timeout is handled by OverpassClient so it can fall back to the next endpoint.
        services.AddHttpClient<IOverpassClient, OverpassClient>(http => http.Timeout = Timeout.InfiniteTimeSpan);
        services.TryAddTransient<StationCatalog>();
        services.TryAddTransient<BoundaryService>();
        services.TryAddTransient<PlaceCatalog>();
        return services;
    }
}
