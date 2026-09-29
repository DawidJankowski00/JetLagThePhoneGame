using TransitGames.UI.Resources;

namespace TransitGames.UI.Services;

/// <summary>Localized formatting of distances and durations shown in lists and headers.</summary>
public static class DisplayFormat
{
    public static string Distance(double meters) => meters < 1000
        ? string.Format(UiStrings.MetersFormat, Math.Round(meters))
        : string.Format(UiStrings.KilometersFormat, meters / 1000);

    public static string Duration(TimeSpan duration) =>
        duration < TimeSpan.Zero ? "0:00:00" : $"{(int)duration.TotalHours}:{duration:mm\\:ss}";
}
