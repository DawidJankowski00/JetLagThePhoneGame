namespace TransitGames.Core.Geography;

/// <summary>A game area remembered for reuse (works offline, no need to draw/download it again).</summary>
public sealed record SavedGameArea(string Key, GameAreaDefinition Definition, DateTimeOffset LastUsedAt);

public interface IGameAreaRepository
{
    /// <summary>Saves or refreshes an area; <paramref name="key"/> identifies its geometry (see TransitGames.Geo).</summary>
    Task SaveAsync(string key, GameAreaDefinition definition, DateTimeOffset usedAt, CancellationToken cancellationToken = default);

    /// <summary>Saved areas, most recently used first.</summary>
    Task<IReadOnlyList<SavedGameArea>> ListAsync(CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
