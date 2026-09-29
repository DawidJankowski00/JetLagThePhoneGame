using TransitGames.Core.Geography;

namespace TransitGames.Core.Games;

/// <summary>
/// Base class for per-game settings. Concrete settings must be JSON-serializable with System.Text.Json,
/// because they are persisted as part of the session snapshot.
/// </summary>
public abstract class GameSettings
{
    public GameAreaDefinition? Area { get; set; }

    /// <summary>Ids of stations selected for this game (empty = all enabled stations in the area).</summary>
    public List<string> StationIds { get; set; } = [];
}
