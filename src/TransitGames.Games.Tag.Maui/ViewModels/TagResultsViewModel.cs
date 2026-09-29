using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TransitGames.Games.Tag.Maui.Services;
using TransitGames.Games.Tag.Resources;

namespace TransitGames.Games.Tag.Maui.ViewModels;

public sealed record RankRow(string Title, string Detail, bool IsWinner);

public sealed record ResultRow(string Title, string Detail, string Value);

/// <summary>
/// The winner, the ranking of destinations by distance from the runner, the history of turns (who ran, how long,
/// coins spent and earned, who caught), everybody's coins and the local player's transactions.
/// </summary>
public sealed partial class TagResultsViewModel : ObservableObject
{
    private readonly TagController _game;

    public TagResultsViewModel(TagController game) => _game = game;

    [ObservableProperty]
    public partial string Headline { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? EndDetail { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<RankRow> Ranking { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ResultRow> Turns { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ResultRow> Balances { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ResultRow> Transactions { get; set; } = [];

    public void OnAppearing()
    {
        if (_game.Session is not { } session)
        {
            return;
        }

        var now = _game.Now;
        var results = session.Results(now);
        Headline = TagTexts.Headline(results);
        EndDetail = results.PointSource is { } source && results.Reason == GameEndReason.TimeUp
            ? string.Format(TagStrings.ResultsPointSourceFormat, TagTexts.PointSource(source))
            : results.IsOver ? null : TagStrings.ResultsRunningHint;

        Ranking = [.. results.Ranking.Select((r, i) => new RankRow(
            string.Format(TagStrings.RankRowFormat, i + 1, r.Player.Name),
            !session.CanSeeDestination(r.Player) ? TagStrings.DestinationHidden
            : r.DistanceMeters is { } distance ? string.Format(TagStrings.RankDistanceFormat, r.Player.Destination?.Name, TagTexts.Distance(distance))
            : r.Player.Destination?.Name ?? TagStrings.DestinationMissing,
            r.IsWinner))];

        Turns = [.. results.Turns.Select(t => new ResultRow(
            string.Format(TagStrings.TurnRowTitleFormat, t.Index + 1, t.Runner.Name),
            string.Format(TagStrings.TurnRowDetailsFormat, TagTexts.Duration(t.Duration), TagTexts.TurnEnd(t), t.Spent, t.Earned)
                + (t.Bonus > 0 ? " · " + string.Format(TagStrings.TurnBonusDetailFormat, t.Bonus) : string.Empty)
                + (t.Forced ? " · " + TagStrings.TurnForced : string.Empty),
            t.StartedAt.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)))];

        Balances = [.. session.Players
            .OrderByDescending(p => session.Balance(p.Id))
            .Select(p => new ResultRow(p.Id == session.LocalPlayerId ? string.Format(TagStrings.YouFormat, p.Name) : p.Name, string.Empty, TagTexts.Coins(session.Balance(p.Id))))];

        Transactions = [.. session.TransactionsOf(session.LocalPlayerId)
            .OrderByDescending(t => t.At)
            .Select(t => new ResultRow(
                TagTexts.TransactionKind(t.Kind),
                string.Join(" · ", new[] { t.At.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture), t.Note }.Where(s => !string.IsNullOrEmpty(s))),
                TagTexts.SignedCoins(t.Amount)))];
    }
}
