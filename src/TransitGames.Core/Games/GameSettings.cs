using TransitGames.Core.Geography;
using TransitGames.Core.Stations;

namespace TransitGames.Core.Games;

/// <summary>
/// Base class for per-game settings. Concrete settings must be JSON-serializable with System.Text.Json,
/// because they are persisted as part of the session snapshot.
/// </summary>
public abstract class GameSettings
{
    public GameAreaDefinition? Area { get; set; }

    /// <summary>
    /// Enabled stations of the area, copied when the game starts. The session keeps its own copy so a resumed game
    /// is unaffected by later edits or re-downloads of the station cache.
    /// </summary>
    public List<Station> Stations { get; set; } = [];
}
