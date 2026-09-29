using TransitGames.Core.Games;
using TransitGames.Core.Sessions;
using TransitGames.Games.Tag.Resources;

namespace TransitGames.Games.Tag;

public sealed class TagModule : GameModule<TagSettings, TagSession>
{
    public const string GameId = "tag";

    public override string Id => GameId;
    public override string DisplayName => TagStrings.GameName;
    public override string Description => TagStrings.GameDescription;
    public override string Icon => "🏃";
    public override int MinPlayers => 2;

    public override IReadOnlyList<GameRole> Roles { get; } =
    [
        new(TagRoles.Runner, TagStrings.RoleRunner, TagStrings.RoleRunnerDescription),
        new(TagRoles.Chaser, TagStrings.RoleChaser, TagStrings.RoleChaserDescription),
    ];

    /// <summary>Chasers may see the runners (pings); the chasers' location is never shared.</summary>
    public override LocationSharingPolicy LocationSharing { get; } =
        new([new LocationSharingRule(TagRoles.Chaser, TagRoles.Runner)]);

    protected override TagSession CreateSession(TagSettings settings, string localRoleId) => new(settings, localRoleId);

    protected override TagSession RestoreSession(GameSessionSnapshot snapshot, TagSettings settings) =>
        new(snapshot, settings);
}

public static class TagRoles
{
    public const string Runner = "runner";
    public const string Chaser = "chaser";
}
