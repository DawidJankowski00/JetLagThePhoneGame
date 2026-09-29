using TransitGames.Core.Geography;
using TransitGames.Games.Tag.Challenges;
using TransitGames.Geo.Areas;

namespace TransitGames.Games.Tag.Targets;

/// <summary>
/// Draws targets: random but spread over the area (a minimum spacing that is relaxed only when there are not
/// enough candidates), rewards growing with the distance from the centre, challenges handed out at random.
/// Deterministic for a given seed.
/// </summary>
public static class TargetGenerator
{
    /// <summary>Reward of a target by distance from the area centre: near, middle, far.</summary>
    public static readonly IReadOnlyList<int> RewardSteps = [5, 10, 15];

    public const int ManualReward = 10;

    public static List<TagTarget> Generate(
        IReadOnlyList<TargetCandidate> candidates,
        GameArea area,
        int count,
        int seed,
        ChallengeDeck? challenges = null)
    {
        var random = new Random(seed);
        var inside = candidates
            .Where(c => area.Contains(c.Location))
            .DistinctBy(c => c.Id)
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();
        Shuffle(inside, random);

        var chosen = new List<TargetCandidate>();
        var spacing = count > 0 ? Math.Sqrt(area.LocalGeometry.Area / count) / 2 : 0;
        while (chosen.Count < count && chosen.Count < inside.Count)
        {
            foreach (var candidate in inside)
            {
                if (chosen.Count >= count)
                {
                    break;
                }

                if (!chosen.Contains(candidate) && chosen.All(c => c.Location.DistanceTo(candidate.Location) >= spacing))
                {
                    chosen.Add(candidate);
                }
            }

            spacing = spacing < 1 ? 0 : spacing / 2; // not enough far-apart candidates: accept closer ones
        }

        var farthest = chosen.Count == 0 ? 1 : Math.Max(1, chosen.Max(c => c.Location.DistanceTo(area.Center)));
        var targets = chosen
            .Select(c => new TagTarget(c.Id, c.Name, c.Location, RewardFor(c.Location.DistanceTo(area.Center) / farthest)))
            .ToList();

        if (challenges is not null)
        {
            targets = WithChallenges(targets, area, challenges, random);
        }

        return targets;
    }

    /// <summary>A target placed by hand.</summary>
    public static TagTarget Manual(GeoPoint location, string name) =>
        new($"manual/{Guid.NewGuid():N}", name, location, ManualReward);

    /// <summary>
    /// Hands out challenges that can be done anywhere (each at most once while there are enough) and adds the
    /// challenges tied to a place inside the area as targets of their own.
    /// </summary>
    public static List<TagTarget> WithChallenges(IReadOnlyList<TagTarget> targets, GameArea area, ChallengeDeck challenges, Random random)
    {
        var pool = challenges.Anywhere.ToList();
        Shuffle(pool, random);
        var result = targets
            .Select((t, i) => pool.Count == 0 || t.Challenge is not null
                ? t
                : t with { Challenge = pool[i % pool.Count], Reward = t.Reward + pool[i % pool.Count].Reward })
            .ToList();

        foreach (var located in challenges.Located.Where(c => area.Contains(c.Location!.Point)))
        {
            result.Add(new TagTarget(
                $"challenge/{located.Id}",
                located.Location!.Name ?? located.LocalizedTitle,
                located.Location.Point,
                ManualReward + located.Reward,
                located));
        }

        return result;
    }

    private static int RewardFor(double fractionOfMaxDistance) =>
        RewardSteps[Math.Clamp((int)(fractionOfMaxDistance * RewardSteps.Count), 0, RewardSteps.Count - 1)];

    private static void Shuffle<T>(List<T> items, Random random)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
