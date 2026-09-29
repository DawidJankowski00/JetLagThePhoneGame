namespace TransitGames.Core.Stations;

/// <summary>Stations of a game area, cached locally so games work offline once downloaded.</summary>
public interface IStationRepository
{
    /// <summary>Stations stored for the given area key (see TransitGames.Geo for how keys are derived).</summary>
    Task<IReadOnlyList<Station>> GetStationsAsync(string areaKey, CancellationToken cancellationToken = default);

    /// <summary>Replaces downloaded stations of an area, keeping custom stations and enabled flags.</summary>
    Task ReplaceDownloadedAsync(string areaKey, IReadOnlyList<Station> stations, CancellationToken cancellationToken = default);

    Task UpsertAsync(string areaKey, Station station, CancellationToken cancellationToken = default);

    Task SetEnabledAsync(string areaKey, string stationId, bool isEnabled, CancellationToken cancellationToken = default);

    Task DeleteAsync(string areaKey, string stationId, CancellationToken cancellationToken = default);
}
