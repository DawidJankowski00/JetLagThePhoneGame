using TransitGames.Core.Games;
using TransitGames.Core.Sessions;
using TransitGames.Games.HideAndSeek.Resources;

namespace TransitGames.Games.HideAndSeek;

public sealed class HideAndSeekModule : GameModule<HideAndSeekSettings, HideAndSeekSession>
{
    public const string GameId = "hide-and-seek";

    public override string Id => GameId;
    public override string DisplayName => HideAndSeekStrings.GameName;
    public override string Description => HideAndSeekStrings.GameDescription;
    public override string Icon => "🔍";
    public override int MinPlayers => 2;

    public override IReadOnlyList<GameRole> Roles { get; } =
    [
        new(HideAndSeekRoles.Hider, HideAndSeekStrings.RoleHider, HideAndSeekStrings.RoleHiderDescription),
        new(HideAndSeekRoles.Seeker, HideAndSeekStrings.RoleSeeker, HideAndSeekStrings.RoleSeekerDescription),
    ];

    /// <summary>Hiders may see the seekers; the hiders' location is never shared.</summary>
    public override LocationSharingPolicy LocationSharing { get; } =
        new([new LocationSharingRule(HideAndSeekRoles.Hider, HideAndSeekRoles.Seeker)]);

    protected override HideAndSeekSession CreateSession(HideAndSeekSettings settings, string localRoleId) =>
        new(settings, localRoleId);

    protected override HideAndSeekSession RestoreSession(GameSessionSnapshot snapshot, HideAndSeekSettings settings) =>
        new(snapshot, settings);
}

public static class HideAndSeekRoles
{
    public const string Hider = "hider";
    public const string Seeker = "seeker";
}
