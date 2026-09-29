using TransitGames.Core.Games;
using TransitGames.Core.Stations;
using TransitGames.Geo.Areas;

namespace TransitGames.App.Services;

/// <summary>State shared by the new-game wizard pages (role → area → stations → game settings → start).</summary>
public sealed class NewGameWizard
{
    public IGameModule? Module { get; private set; }

    public GameRole? Role { get; set; }

    public GameArea? Area { get; set; }

    /// <summary>Enabled stations chosen in the stations step.</summary>
    public IReadOnlyList<Station> Stations { get; set; } = [];

    /// <summary>The game's settings, edited by the game's own settings view.</summary>
    public GameSettings? Settings { get; private set; }

    public void Begin(IGameModule module)
    {
        Module = module;
        Role = null;
        Area = null;
        Stations = [];
        Settings = module.CreateDefaultSettings();
    }

    public GameSettings RequireSettings() =>
        Settings ?? throw new InvalidOperationException("The new-game wizard was not started.");

    public IGameModule RequireModule() =>
        Module ?? throw new InvalidOperationException("The new-game wizard was not started.");

    public GameArea RequireArea() =>
        Area ?? throw new InvalidOperationException("The game area has not been chosen.");
}
