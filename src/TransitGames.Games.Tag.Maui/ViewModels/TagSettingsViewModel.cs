using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TransitGames.Core.Geography;
using TransitGames.Games.Tag.Resources;
using TransitGames.Games.Tag.Targets;
using TransitGames.Geo.Areas;
using TransitGames.Geo.Overpass;
using TransitGames.Geo.Places;
using TransitGames.UI.Services;

namespace TransitGames.Games.Tag.Maui.ViewModels;

public sealed record SettingsChoice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Edits <see cref="TagSettings"/> in place (new-game wizard): rounds and times, rewards, and the targets — drawn
/// from stations or OSM places, or placed by hand (<see cref="TargetsEditorViewModel"/>). Targets are kept without
/// challenges in <see cref="BaseTargets"/>; the challenges are added on top, so switching them off and on is lossless.
/// </summary>
public sealed partial class TagSettingsViewModel : ObservableObject
{
    private static readonly string[] PlaceCategoryIds = ["park", "museum", "library", "cinema", "theatre", "university", "hospital", "zoo"];

    private readonly TagSettings _settings;
    private readonly PlaceCatalog _places;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly ILogger<TagSettingsViewModel> _logger;
    private readonly GameArea? _area;
    private int _seed = Random.Shared.Next();

    public TagSettingsViewModel(
        TagSettings settings,
        PlaceCatalog places,
        IDialogService dialogs,
        INavigationService navigation,
        ILogger<TagSettingsViewModel> logger)
    {
        _settings = settings;
        _places = places;
        _dialogs = dialogs;
        _navigation = navigation;
        _logger = logger;
        _area = settings.Area is { } definition ? GameArea.FromDefinition(definition) : null;

        BaseTargets = [.. settings.Targets.Select(t => t.Challenge is null ? t : t with { Challenge = null, Reward = t.Reward - t.Challenge.Reward })
            .Where(t => !t.Id.StartsWith("challenge/", StringComparison.Ordinal))];

        RoundCounts = [.. new[] { 1, 2, 3, 4, 6 }.Select(n => new SettingsChoice<int>(n, n.ToString(System.Globalization.CultureInfo.CurrentCulture)))];
        RoundDurations = Minutes(30, 45, 60, 90, 120, 180);
        HeadStarts = Minutes(0, 5, 10, 15, 20, 30);
        RevealIntervals = Minutes(5, 10, 15, 20, 30);
        TargetRadii = Meters(50, 100, 150, 250, 400);
        CatchRadii = Meters(10, 25, 50, 100);
        RewardKinds = [new(RewardKind.Points, TagStrings.RewardKindPoints), new(RewardKind.Coins, TagStrings.RewardKindCoins)];
        Sources = [.. Enum.GetValues<TargetSource>().Select(s => new SettingsChoice<TargetSource>(s, TagTexts.Source(s)))];
        Categories = [.. PlaceCategoryIds.Select(id => new SettingsChoice<string>(id, TagTexts.PlaceCategory(id)))];
        TargetCounts = [.. new[] { 3, 4, 5, 6, 8, 10, 12 }.Select(n => new SettingsChoice<int>(n, n.ToString(System.Globalization.CultureInfo.CurrentCulture)))];

        SelectedRounds = Closest(RoundCounts, settings.Rounds, (a, b) => Math.Abs(a - b));
        SelectedRoundDuration = Closest(RoundDurations, settings.RoundDuration, (a, b) => Math.Abs((a - b).TotalMinutes));
        SelectedHeadStart = Closest(HeadStarts, settings.HeadStart, (a, b) => Math.Abs((a - b).TotalMinutes));
        SelectedRevealInterval = Closest(RevealIntervals, settings.RevealInterval, (a, b) => Math.Abs((a - b).TotalMinutes));
        SelectedTargetRadius = Closest(TargetRadii, settings.TargetRadiusMeters, (a, b) => Math.Abs(a - b));
        SelectedCatchRadius = Closest(CatchRadii, settings.CatchRadiusMeters, (a, b) => Math.Abs(a - b));
        SelectedRewardKind = RewardKinds.First(k => k.Value == settings.RewardKind);
        SelectedSource = Sources.First(s => s.Value == settings.TargetSource);
        SelectedCategory = Categories.FirstOrDefault(c => c.Value == settings.PlaceCategoryId) ?? Categories[0];
        SelectedTargetCount = Closest(TargetCounts, settings.TargetCount, (a, b) => Math.Abs(a - b));
        AutoCapture = settings.AutoCapture;
        ChallengesEnabled = settings.ChallengesEnabled;

        if (BaseTargets.Count == 0 && settings.TargetSource == TargetSource.Stations)
        {
            DrawFromStations();
        }

        UpdateSummary();
    }

    public TagSettings Settings => _settings;

    public GameArea? Area => _area;

    /// <summary>Targets without challenges; <see cref="TagSettings.Targets"/> = these plus challenges.</summary>
    public List<TagTarget> BaseTargets { get; private set; }

    public IReadOnlyList<SettingsChoice<int>> RoundCounts { get; }
    public IReadOnlyList<SettingsChoice<TimeSpan>> RoundDurations { get; }
    public IReadOnlyList<SettingsChoice<TimeSpan>> HeadStarts { get; }
    public IReadOnlyList<SettingsChoice<TimeSpan>> RevealIntervals { get; }
    public IReadOnlyList<SettingsChoice<double>> TargetRadii { get; }
    public IReadOnlyList<SettingsChoice<double>> CatchRadii { get; }
    public IReadOnlyList<SettingsChoice<RewardKind>> RewardKinds { get; }
    public IReadOnlyList<SettingsChoice<TargetSource>> Sources { get; }
    public IReadOnlyList<SettingsChoice<string>> Categories { get; }
    public IReadOnlyList<SettingsChoice<int>> TargetCounts { get; }

    [ObservableProperty]
    public partial SettingsChoice<int> SelectedRounds { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<TimeSpan> SelectedRoundDuration { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<TimeSpan> SelectedHeadStart { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<TimeSpan> SelectedRevealInterval { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<double> SelectedTargetRadius { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<double> SelectedCatchRadius { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<RewardKind> SelectedRewardKind { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaces), nameof(CanDraw))]
    public partial SettingsChoice<TargetSource> SelectedSource { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<string> SelectedCategory { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<int> SelectedTargetCount { get; set; }

    [ObservableProperty]
    public partial bool AutoCapture { get; set; }

    [ObservableProperty]
    public partial bool ChallengesEnabled { get; set; }

    [ObservableProperty]
    public partial string TargetsSummary { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(DrawCommand))]
    public partial string? BusyText { get; set; }

    public bool IsBusy => BusyText is not null;

    public bool IsPlaces => SelectedSource.Value == TargetSource.Places;

    public bool CanDraw => SelectedSource.Value != TargetSource.Manual;

    partial void OnSelectedRoundsChanged(SettingsChoice<int> value) => _settings.Rounds = value.Value;

    partial void OnSelectedRoundDurationChanged(SettingsChoice<TimeSpan> value) => _settings.RoundDuration = value.Value;

    partial void OnSelectedHeadStartChanged(SettingsChoice<TimeSpan> value) => _settings.HeadStart = value.Value;

    partial void OnSelectedRevealIntervalChanged(SettingsChoice<TimeSpan> value) => _settings.RevealInterval = value.Value;

    partial void OnSelectedTargetRadiusChanged(SettingsChoice<double> value) => _settings.TargetRadiusMeters = value.Value;

    partial void OnSelectedCatchRadiusChanged(SettingsChoice<double> value) => _settings.CatchRadiusMeters = value.Value;

    partial void OnSelectedRewardKindChanged(SettingsChoice<RewardKind> value)
    {
        _settings.RewardKind = value.Value;
        UpdateSummary();
    }

    partial void OnSelectedSourceChanged(SettingsChoice<TargetSource> value) => _settings.TargetSource = value.Value;

    partial void OnSelectedCategoryChanged(SettingsChoice<string> value) => _settings.PlaceCategoryId = value.Value;

    partial void OnSelectedTargetCountChanged(SettingsChoice<int> value) => _settings.TargetCount = value.Value;

    partial void OnAutoCaptureChanged(bool value) => _settings.AutoCapture = value;

    partial void OnChallengesEnabledChanged(bool value)
    {
        _settings.ChallengesEnabled = value;
        ApplyTargets();
    }

    /// <summary>Replaces the targets (from the map editor) and re-applies the challenges.</summary>
    public void SetBaseTargets(IEnumerable<TagTarget> targets)
    {
        BaseTargets = [.. targets];
        ApplyTargets();
    }

    public void SetFinalTarget(GeoPoint? point)
    {
        _settings.FinalTarget = point is { } p ? new TagTarget("final", TagStrings.FinalTargetName, p, 0) : null;
        UpdateSummary();
    }

    private bool CanDrawNow() => BusyText is null;

    [RelayCommand(CanExecute = nameof(CanDrawNow))]
    private async Task DrawAsync()
    {
        _seed = Random.Shared.Next();
        if (SelectedSource.Value == TargetSource.Stations)
        {
            DrawFromStations();
            return;
        }

        if (_area is null)
        {
            return;
        }

        try
        {
            BusyText = TagStrings.SettingsDownloadingPlaces;
            var places = await _places.GetAsync(_area, _settings.Stations, SelectedCategory.Value, TagTexts.PlaceCategory(SelectedCategory.Value));
            var candidates = places.Select(p => new TargetCandidate(p.Id, p.Name, p.Location)).ToList();
            if (candidates.Count == 0)
            {
                await _dialogs.AlertAsync(TagStrings.SettingsTargets, TagStrings.SettingsNoPlaces);
                return;
            }

            SetBaseTargets(TargetGenerator.Generate(candidates, _area, _settings.TargetCount, _seed));
        }
        catch (OverpassException ex)
        {
            _logger.LogWarning(ex, "Downloading places for targets failed");
            await _dialogs.AlertAsync(TagStrings.SettingsTargets, TagStrings.SettingsPlacesFailed);
        }
        finally
        {
            BusyText = null;
        }
    }

    [RelayCommand]
    private Task EditOnMapAsync() =>
        _navigation.GoToAsync(TagUiModule.TargetsEditorRoute, new Dictionary<string, object> { [TargetsEditorViewModel.SettingsParameter] = this });

    private void DrawFromStations()
    {
        if (_area is null)
        {
            return;
        }

        var candidates = _settings.Stations.Where(s => s.IsEnabled).Select(s => new TargetCandidate(s.Id, s.Name, s.Location)).ToList();
        SetBaseTargets(TargetGenerator.Generate(candidates, _area, _settings.TargetCount, _seed));
    }

    private void ApplyTargets()
    {
        _settings.Targets = _settings.ChallengesEnabled && _area is not null
            ? TargetGenerator.WithChallenges(BaseTargets, _area, _settings.LoadChallenges(), new Random(_seed))
            : [.. BaseTargets];
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var targets = _settings.Targets;
        var challenges = targets.Count(t => t.Challenge is not null);
        TargetsSummary = string.Format(
            TagStrings.SettingsTargetsSummaryFormat,
            targets.Count,
            challenges,
            TagTexts.Reward(targets.Sum(t => t.Reward), _settings.RewardKind),
            _settings.FinalTarget is null ? TagStrings.SettingsNoFinal : TagStrings.SettingsWithFinal);
    }

    private static IReadOnlyList<SettingsChoice<TimeSpan>> Minutes(params int[] values) =>
        [.. values.Select(m => new SettingsChoice<TimeSpan>(TimeSpan.FromMinutes(m), string.Format(TagStrings.MinutesFormat, m)))];

    private static IReadOnlyList<SettingsChoice<double>> Meters(params int[] values) =>
        [.. values.Select(m => new SettingsChoice<double>(m, TagTexts.Distance(m)))];

    private static SettingsChoice<T> Closest<T, TDistance>(IReadOnlyList<SettingsChoice<T>> choices, T value, Func<T, T, TDistance> distance)
        where TDistance : IComparable<TDistance> =>
        choices.MinBy(c => distance(c.Value, value))!;
}
