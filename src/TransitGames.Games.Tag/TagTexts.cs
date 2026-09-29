using System.Globalization;
using TransitGames.Games.Tag.Resources;

namespace TransitGames.Games.Tag;

/// <summary>Localized texts built from game data.</summary>
public static class TagTexts
{
    public static string TeamName(TagTeam team) => team == TagTeam.First ? TagStrings.TeamFirst : TagStrings.TeamSecond;

    /// <summary>"Team 1 (you)" for the local team.</summary>
    public static string TeamName(TagTeam team, TagTeam localTeam) =>
        team == localTeam ? string.Format(TagStrings.TeamYouFormat, TeamName(team)) : TeamName(team);

    public static string Duration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return duration.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{duration.Minutes}:{duration.Seconds:00}");
    }

    public static string Distance(double meters) =>
        meters >= 1000
            ? string.Format(CultureInfo.CurrentCulture, "{0:0.0} km", meters / 1000)
            : string.Format(CultureInfo.CurrentCulture, "{0:0} m", meters);

    public static string Reward(int amount, RewardKind kind) =>
        string.Format(kind == RewardKind.Coins ? TagStrings.RewardCoinsFormat : TagStrings.RewardPointsFormat, amount);

    public static string EndReason(RoundEndReason? reason) => reason switch
    {
        RoundEndReason.Caught => TagStrings.EndCaught,
        RoundEndReason.TimeUp => TagStrings.EndTimeUp,
        RoundEndReason.ReachedFinal => TagStrings.EndReachedFinal,
        _ => TagStrings.EndRunning,
    };

    public static string Source(TargetSource source) => source switch
    {
        TargetSource.Stations => TagStrings.SourceStations,
        TargetSource.Places => TagStrings.SourcePlaces,
        _ => TagStrings.SourceManual,
    };

    public static string PlaceCategory(string categoryId) =>
        TagStrings.ResourceManager.GetString("Category_" + categoryId, TagStrings.Culture) ?? categoryId;
}
