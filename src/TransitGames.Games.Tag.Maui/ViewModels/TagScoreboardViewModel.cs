using CommunityToolkit.Mvvm.ComponentModel;
using TransitGames.Games.Tag.Maui.Services;
using TransitGames.Games.Tag.Resources;

namespace TransitGames.Games.Tag.Maui.ViewModels;

public sealed record TeamRow(string Name, string Score, string Details, bool IsLeader);

public sealed record RoundRow(string Title, string Details, string Score);

/// <summary>Scores of both teams and every round (a running round counts until now).</summary>
public sealed partial class TagScoreboardViewModel : ObservableObject
{
    private readonly TagController _game;

    public TagScoreboardViewModel(TagController game) => _game = game;

    [ObservableProperty]
    public partial IReadOnlyList<TeamRow> Teams { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<RoundRow> Rounds { get; set; } = [];

    [ObservableProperty]
    public partial string RulesText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOfflineHint))]
    public partial string? OfflineHint { get; set; }

    public bool HasOfflineHint => OfflineHint is not null;

    public void OnAppearing()
    {
        if (_game.Session is not { } session)
        {
            return;
        }

        var board = session.Scoreboard(_game.Now);
        var local = session.LocalTeam;
        Teams = board.Teams
            .OrderByDescending(t => t.Score)
            .Select(t => new TeamRow(
                TagTexts.TeamName(t.Team, local),
                string.Format(TagStrings.ScoreFormat, t.Score),
                string.Format(TagStrings.TeamDetailsFormat, TagTexts.Duration(t.Survived), t.Captures, t.Points),
                board.Leader == t.Team))
            .ToList();
        Rounds = board.Rounds
            .Select(r => new RoundRow(
                string.Format(TagStrings.RoundRowTitleFormat, r.Index + 1, TagTexts.TeamName(r.Runners, local)),
                string.Format(
                    TagStrings.RoundRowDetailsFormat,
                    TagTexts.Duration(r.Survived),
                    TagTexts.RoundEnd(r),
                    r.Captures,
                    r.TargetPoints + r.FinalBonus),
                string.Format(TagStrings.ScoreFormat, r.Score)))
            .ToList();
        RulesText = session.Settings.RewardKind == RewardKind.Points ? TagStrings.ScoreRulesPoints : TagStrings.ScoreRulesCoins;
        OfflineHint = _game.IsOnline ? null : TagStrings.ScoreOfflineHint;
    }
}
