using TransitGames.Core.Geography;

namespace TransitGames.Games.Tag.Destinations;

/// <summary>How the players' destinations are chosen (game setting).</summary>
public enum DestinationMode
{
    /// <summary>Every player picks their own on the map before the start.</summary>
    SelfPick,

    /// <summary>Drawn from the game's stations when the host starts the game.</summary>
    RandomStations,

    /// <summary>The host sets all of them.</summary>
    HostSets,
}

/// <summary>A place a destination can be drawn from (a station of the game).</summary>
public sealed record DestinationCandidate(string Id, string Name, GeoPoint Location);

/// <summary>Limits that keep drawn destinations fair.</summary>
/// <param name="MinDistanceMeters">Minimum distance from the start point.</param>
/// <param name="MaxDistanceMeters">Maximum distance from the start point (0 = no limit).</param>
/// <param name="MinSpacingMeters">Minimum distance between two destinations.</param>
public sealed record DestinationRules(double MinDistanceMeters, double MaxDistanceMeters, double MinSpacingMeters);

/// <summary>
/// Draws one destination per player from the candidates, within the distance band around the start and at least the
/// spacing apart. Deterministic for a given <see cref="Random"/> seed.
/// </summary>
public static class DestinationGenerator
{
    private const int Attempts = 64;

    /// <returns>Destinations in player order, or null when the rules cannot be met with these candidates.</returns>
    public static IReadOnlyList<DestinationCandidate>? Draw(
        IReadOnlyList<DestinationCandidate> candidates,
        GeoPoint start,
        int count,
        DestinationRules rules,
        Random random)
    {
        var inBand = candidates
            .DistinctBy(c => c.Id)
            .Where(c => IsInBand(c.Location, start, rules))
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();
        if (inBand.Count < count)
        {
            return null;
        }

        // Greedy picks from random orders; a few attempts find a spread-out set when one exists.
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            Shuffle(inBand, random);
            var chosen = new List<DestinationCandidate>(count);
            foreach (var candidate in inBand)
            {
                if (chosen.All(c => c.Location.DistanceTo(candidate.Location) >= rules.MinSpacingMeters))
                {
                    chosen.Add(candidate);
                    if (chosen.Count == count)
                    {
                        return chosen;
                    }
                }
            }
        }

        return null;
    }

    public static bool IsInBand(GeoPoint point, GeoPoint start, DestinationRules rules)
    {
        var distance = point.DistanceTo(start);
        return distance >= rules.MinDistanceMeters && (rules.MaxDistanceMeters <= 0 || distance <= rules.MaxDistanceMeters);
    }

    private static void Shuffle<T>(List<T> items, Random random)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
