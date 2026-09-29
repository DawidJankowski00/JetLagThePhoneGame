namespace TransitGames.Core.Games;

/// <summary>Result of a game (or its current standing) for the history.</summary>
/// <param name="Headline">One line, e.g. "Team 1 wins 23 : 12" or "Hidden for 1:23:00".</param>
public sealed record GameResult(string Headline, IReadOnlyList<GameResultLine> Lines)
{
    public static GameResult Empty { get; } = new(string.Empty, []);
}

public sealed record GameResultLine(string Label, string Value);
