using TransitGames.Core.Games;

namespace TransitGames.Games.HideAndSeek;

public enum GameSize
{
    Small,
    Medium,
    Large,
}

public sealed class HideAndSeekSettings : GameSettings
{
    public GameSize Size { get; set; } = GameSize.Medium;

    /// <summary>Radius of the hiding zone around the chosen station, in meters.</summary>
    public double HidingZoneRadiusMeters { get; set; } = DefaultRadius(GameSize.Medium);

    public TimeSpan HidingTime { get; set; } = TimeSpan.FromMinutes(45);

    public static double DefaultRadius(GameSize size) => size switch
    {
        GameSize.Small => 500,
        GameSize.Medium => 1000,
        GameSize.Large => 1500,
        _ => 1000,
    };
}
