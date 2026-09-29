using CommunityToolkit.Mvvm.ComponentModel;
using TransitGames.Games.HideAndSeek.Questions;
using TransitGames.Games.HideAndSeek.Resources;

namespace TransitGames.Games.HideAndSeek.Maui.ViewModels;

public sealed record SettingsChoice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A question type that can be switched on or off for the game.</summary>
public sealed partial class QuestionTypeToggle : ObservableObject
{
    private readonly HideAndSeekSettings _settings;

    public QuestionTypeToggle(HideAndSeekSettings settings, QuestionType type)
    {
        _settings = settings;
        Type = type;
        var rule = settings.Rule(type);
        Name = QuestionTexts.TypeName(type);
        Details = string.Format(HideAndSeekStrings.RuleFormat, rule.CardsDrawn, rule.CardsKept, (int)rule.AnswerTime.TotalMinutes);
        IsEnabled = rule.Enabled;
    }

    public QuestionType Type { get; }

    public string Name { get; }

    public string Details { get; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    partial void OnIsEnabledChanged(bool value) =>
        _settings.Questions[Type] = _settings.Rule(Type) with { Enabled = value };
}

/// <summary>Edits <see cref="HideAndSeekSettings"/> in place (new-game wizard).</summary>
public sealed partial class HideAndSeekSettingsViewModel : ObservableObject
{
    private readonly HideAndSeekSettings _settings;

    public HideAndSeekSettingsViewModel(HideAndSeekSettings settings)
    {
        _settings = settings;
        Sizes =
        [
            new(GameSize.Small, string.Format(HideAndSeekStrings.SizeSmallFormat, QuestionTexts.Distance(HideAndSeekSettings.DefaultRadius(GameSize.Small)))),
            new(GameSize.Medium, string.Format(HideAndSeekStrings.SizeMediumFormat, QuestionTexts.Distance(HideAndSeekSettings.DefaultRadius(GameSize.Medium)))),
            new(GameSize.Large, string.Format(HideAndSeekStrings.SizeLargeFormat, QuestionTexts.Distance(HideAndSeekSettings.DefaultRadius(GameSize.Large)))),
        ];
        HidingTimes = new[] { 15, 30, 45, 60, 90, 120 }
            .Select(m => new SettingsChoice<TimeSpan>(TimeSpan.FromMinutes(m), string.Format(HideAndSeekStrings.MinutesFormat, m)))
            .ToList();
        QuestionTypes = Enum.GetValues<QuestionType>().Select(t => new QuestionTypeToggle(settings, t)).ToList();

        SelectedSize = Sizes.First(s => s.Value == settings.Size);
        SelectedHidingTime = HidingTimes.MinBy(t => Math.Abs((t.Value - settings.HidingTime).TotalMinutes))!;
        CardsEnabled = settings.CardsEnabled;
    }

    public IReadOnlyList<SettingsChoice<GameSize>> Sizes { get; }

    public IReadOnlyList<SettingsChoice<TimeSpan>> HidingTimes { get; }

    public IReadOnlyList<QuestionTypeToggle> QuestionTypes { get; }

    [ObservableProperty]
    public partial SettingsChoice<GameSize> SelectedSize { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<TimeSpan> SelectedHidingTime { get; set; }

    [ObservableProperty]
    public partial bool CardsEnabled { get; set; }

    partial void OnSelectedSizeChanged(SettingsChoice<GameSize> value)
    {
        _settings.Size = value.Value;
        _settings.HidingZoneRadiusMeters = HideAndSeekSettings.DefaultRadius(value.Value);
    }

    partial void OnSelectedHidingTimeChanged(SettingsChoice<TimeSpan> value) => _settings.HidingTime = value.Value;

    partial void OnCardsEnabledChanged(bool value) => _settings.CardsEnabled = value;
}
