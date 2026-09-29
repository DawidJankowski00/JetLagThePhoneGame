namespace TransitGames.Core.Games;

/// <summary>A role a team can play in a game, e.g. Hider/Seeker or Runner/Chaser.</summary>
public sealed record GameRole(string Id, string DisplayName, string Description);
