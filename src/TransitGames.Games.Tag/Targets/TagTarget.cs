using TransitGames.Core.Geography;
using TransitGames.Games.Tag.Challenges;

namespace TransitGames.Games.Tag.Targets;

/// <summary>A place the runners try to reach. With a challenge it is captured only after doing the challenge there.</summary>
public sealed record TagTarget(string Id, string Name, GeoPoint Location, int Reward, ChallengeDefinition? Challenge = null);

/// <summary>A place a target can be drawn from (a station, an OSM place).</summary>
public sealed record TargetCandidate(string Id, string Name, GeoPoint Location);
