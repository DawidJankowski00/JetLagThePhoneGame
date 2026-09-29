using TransitGames.Core.Geography;
using TransitGames.Core.Stations;
using TransitGames.Geo.Areas;
using TransitGames.Geo.Overpass;

namespace TransitGames.Geo.Stations;

public sealed record StationDownloadResult(int Found, int Merged, int OutsideArea, IReadOnlyList<Station> Stations);

/// <summary>
/// Stations of a game area: downloads from Overpass once, then serves them from the local cache (offline).
/// User changes (disabled stations, custom stations) survive re-downloads.
/// </summary>
public sealed class StationCatalog
{
    private readonly IStationRepository _repository;
    private readonly IOverpassClient _overpass;

    public StationCatalog(IStationRepository repository, IOverpassClient overpass)
    {
        _repository = repository;
        _overpass = overpass;
    }

    public Task<IReadOnlyList<Station>> GetCachedAsync(GameArea area, CancellationToken cancellationToken = default) =>
        _repository.GetStationsAsync(area.Key, cancellationToken);

    /// <exception cref="OverpassException">No Overpass server answered.</exception>
    public async Task<StationDownloadResult> DownloadAsync(
        GameArea area,
        TransitModes modes,
        CancellationToken cancellationToken = default)
    {
        var (southWest, northEast) = area.Bounds;
        var response = await _overpass.QueryAsync(OverpassQueries.Stations(southWest, northEast, modes), cancellationToken)
            .ConfigureAwait(false);

        var parsed = OverpassStationParser.Parse(response, modes);
        var inside = parsed.Where(s => area.Contains(s.Location)).ToList();
        var merged = StationDeduplicator.Deduplicate(inside, area.Projection);

        await _repository.ReplaceDownloadedAsync(area.Key, merged, cancellationToken).ConfigureAwait(false);
        var stored = await _repository.GetStationsAsync(area.Key, cancellationToken).ConfigureAwait(false);
        return new StationDownloadResult(parsed.Count, inside.Count - merged.Count, parsed.Count - inside.Count, stored);
    }

    /// <exception cref="ArgumentException">The location is outside the game area.</exception>
    public async Task<Station> AddCustomAsync(
        GameArea area,
        string name,
        GeoPoint location,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A station needs a name.", nameof(name));
        }

        if (!area.Contains(location))
        {
            throw new ArgumentException("The station must be inside the game area.", nameof(location));
        }

        var station = new Station
        {
            Id = GeoJsonStationImporter.CustomId(name.Trim(), location),
            Name = name.Trim(),
            Location = location,
            IsCustom = true,
        };
        await _repository.UpsertAsync(area.Key, station, cancellationToken).ConfigureAwait(false);
        return station;
    }

    /// <exception cref="GeoJsonFormatException">The text is not valid GeoJSON.</exception>
    public async Task<StationImportResult> ImportGeoJsonAsync(
        GameArea area,
        string geoJson,
        CancellationToken cancellationToken = default)
    {
        var result = GeoJsonStationImporter.Import(geoJson, area);
        foreach (var station in result.Stations)
        {
            await _repository.UpsertAsync(area.Key, station, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public Task SetEnabledAsync(GameArea area, Station station, bool isEnabled, CancellationToken cancellationToken = default) =>
        _repository.SetEnabledAsync(area.Key, station.Id, isEnabled, cancellationToken);

    /// <summary>Removes a custom station. OSM stations cannot be deleted, only disabled.</summary>
    public Task DeleteCustomAsync(GameArea area, Station station, CancellationToken cancellationToken = default)
    {
        if (!station.IsCustom)
        {
            throw new InvalidOperationException("Only custom stations can be deleted; disable OSM stations instead.");
        }

        return _repository.DeleteAsync(area.Key, station.Id, cancellationToken);
    }
}
