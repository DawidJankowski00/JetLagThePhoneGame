using TransitGames.Core.Games;
using TransitGames.Geo.Areas;

namespace TransitGames.App.Services;

/// <summary>State shared by the new-game wizard pages (role → area → stations → start).</summary>
public sealed class NewGameWizard
{
    public IGameModule? Module { get; private set; }

    public GameRole? Role { get; set; }

    public GameArea? Area { get; set; }

    public void Begin(IGameModule module)
    {
        Module = module;
        Role = null;
        Area = null;
    }

    public IGameModule RequireModule() =>
        Module ?? throw new InvalidOperationException("The new-game wizard was not started.");

    public GameArea RequireArea() =>
        Area ?? throw new InvalidOperationException("The game area has not been chosen.");
}
