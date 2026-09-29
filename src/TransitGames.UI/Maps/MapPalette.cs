using MapsuiColor = Mapsui.Styles.Color;

namespace TransitGames.UI.Maps;

/// <summary>Map layer colours, read from the app resources (TransitGames.UI/Themes/Colors.xaml, keys "Map*").</summary>
public static class MapPalette
{
    public const string PossibleArea = "MapPossibleArea";
    public const string EliminatedArea = "MapEliminatedArea";
    public const string GameAreaOutline = "MapGameAreaOutline";
    public const string Station = "MapStation";
    public const string StationExcluded = "MapStationExcluded";
    public const string HidingZone = "MapHidingZone";
    public const string OwnPosition = "MapOwnPosition";
    public const string Target = "MapTarget";
    public const string TargetCaptured = "MapTargetCaptured";
    public const string Track = "MapTrack";

    public static MapsuiColor Get(string key, double? alpha = null)
    {
        var color = Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color c
            ? c
            : Colors.Magenta; // missing key: loud on purpose
        return new MapsuiColor(
            (int)(color.Red * 255),
            (int)(color.Green * 255),
            (int)(color.Blue * 255),
            (int)((alpha ?? color.Alpha) * 255));
    }
}
