using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TransitGames.Games.Tag.Cards;
using TransitGames.Games.Tag.Coins;
using TransitGames.Games.Tag.Destinations;
using TransitGames.Games.Tag.Players;
using TransitGames.Games.Tag.Resources;
using TransitGames.UI.Services;

namespace TransitGames.Games.Tag.Maui.ViewModels;

public sealed record SettingsChoice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Edits <see cref="TagSettings"/> in place (new-game wizard): times, coins and the turn bonus, who runs next, how
/// destinations are chosen, fares, and the card deck (built-in or loaded from a JSON file).
/// </summary>
public sealed partial class TagSettingsViewModel : ObservableObject
{
    private readonly TagSettings _settings;
    private readonly IDialogService _dialogs;
    private readonly ITextFilePicker _files;
    private readonly ILogger<TagSettingsViewModel> _logger;

    public TagSettingsViewModel(TagSettings settings, IDialogService dialogs, ITextFilePicker files, ILogger<TagSettingsViewModel> logger)
    {
        _settings = settings;
        _dialogs = dialogs;
        _files = files;
        _logger = logger;

        GameDurations = Minutes(60, 90, 120, 180, 240, 300, 360);
        HeadStarts = Minutes(5, 10, 15, 20, 30, 45, 60);
        RevealIntervals = Minutes(5, 10, 15, 20, 30);
        DrawCooldowns = Minutes(0, 5, 10, 15, 20, 30);
        CatchRadii = Meters(10, 25, 50, 100);
        DestinationRadii = Meters(50, 100, 150, 250);
        Coins = Numbers(5, 10, 15, 20, 25, 30, 40, 50);
        BonusAmounts = Numbers(3, 5, 10, 15, 20);
        MinDistances = Meters(0, 500, 1000, 2000, 3000, 5000);
        MaxDistances = [new(0, TagStrings.SettingsNoLimit), .. Meters(3000, 5000, 8000, 10000, 15000, 20000)];
        Spacings = Meters(0, 300, 500, 1000, 2000, 3000);
        BonusModes = [.. Enum.GetValues<TurnBonusMode>().Select(m => new SettingsChoice<TurnBonusMode>(m, TagTexts.TurnBonus(m)))];
        NextRunnerModes = [.. Enum.GetValues<NextRunnerMode>().Select(m => new SettingsChoice<NextRunnerMode>(m, TagTexts.NextRunner(m)))];
        DestinationModes = [.. Enum.GetValues<DestinationMode>().Select(m => new SettingsChoice<DestinationMode>(m, TagTexts.DestinationMode(m)))];
        FareModels = [.. Enum.GetValues<FareModel>().Select(m => new SettingsChoice<FareModel>(m, TagTexts.FareModel(m)))];

        SelectedGameDuration = Closest(GameDurations, settings.GameDuration, (a, b) => Math.Abs((a - b).TotalMinutes));
        SelectedHeadStart = Closest(HeadStarts, settings.HeadStart, (a, b) => Math.Abs((a - b).TotalMinutes));
        SelectedRevealInterval = Closest(RevealIntervals, settings.RevealInterval, (a, b) => Math.Abs((a - b).TotalMinutes));
        SelectedDrawCooldown = Closest(DrawCooldowns, settings.DrawCooldown, (a, b) => Math.Abs((a - b).TotalMinutes));
        SelectedCatchRadius = Closest(CatchRadii, settings.CatchRadiusMeters, (a, b) => Math.Abs(a - b));
        SelectedDestinationRadius = Closest(DestinationRadii, settings.DestinationRadiusMeters, (a, b) => Math.Abs(a - b));
        SelectedStartingCoins = Closest(Coins, settings.StartingCoins, (a, b) => Math.Abs(a - b));
        SelectedBonusMode = BonusModes.First(m => m.Value == settings.TurnBonusMode);
        SelectedTurnBonus = Closest(BonusAmounts, settings.TurnBonus, (a, b) => Math.Abs(a - b));
        SelectedTopUp = Closest(Coins, settings.TopUpMinimum, (a, b) => Math.Abs(a - b));
        SelectedNextRunner = NextRunnerModes.First(m => m.Value == settings.NextRunner);
        SelectedDestinationMode = DestinationModes.First(m => m.Value == settings.DestinationMode);
        SelectedMinDistance = Closest(MinDistances, settings.DestinationMinDistanceMeters, (a, b) => Math.Abs(a - b));
        SelectedMaxDistance = Closest(MaxDistances, settings.DestinationMaxDistanceMeters, (a, b) => Math.Abs(a - b));
        SelectedSpacing = Closest(Spacings, settings.DestinationMinSpacingMeters, (a, b) => Math.Abs(a - b));
        SelectedFareModel = FareModels.First(m => m.Value == settings.FareModel);
        DestinationsVisible = settings.DestinationsVisible;
        UpdateFares();
        UpdateDeck();
    }

    public IReadOnlyList<SettingsChoice<TimeSpan>> GameDurations { get; }
    public IReadOnlyList<SettingsChoice<TimeSpan>> HeadStarts { get; }
    public IReadOnlyList<SettingsChoice<TimeSpan>> RevealIntervals { get; }
    public IReadOnlyList<SettingsChoice<TimeSpan>> DrawCooldowns { get; }
    public IReadOnlyList<SettingsChoice<double>> CatchRadii { get; }
    public IReadOnlyList<SettingsChoice<double>> DestinationRadii { get; }
    public IReadOnlyList<SettingsChoice<int>> Coins { get; }
    public IReadOnlyList<SettingsChoice<int>> BonusAmounts { get; }
    public IReadOnlyList<SettingsChoice<double>> MinDistances { get; }
    public IReadOnlyList<SettingsChoice<double>> MaxDistances { get; }
    public IReadOnlyList<SettingsChoice<double>> Spacings { get; }
    public IReadOnlyList<SettingsChoice<TurnBonusMode>> BonusModes { get; }
    public IReadOnlyList<SettingsChoice<NextRunnerMode>> NextRunnerModes { get; }
    public IReadOnlyList<SettingsChoice<DestinationMode>> DestinationModes { get; }
    public IReadOnlyList<SettingsChoice<FareModel>> FareModels { get; }

    [ObservableProperty]
    public partial SettingsChoice<TimeSpan> SelectedGameDuration { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<TimeSpan> SelectedHeadStart { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<TimeSpan> SelectedRevealInterval { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<TimeSpan> SelectedDrawCooldown { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<double> SelectedCatchRadius { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<double> SelectedDestinationRadius { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<int> SelectedStartingCoins { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFixedBonus), nameof(IsTopUp))]
    public partial SettingsChoice<TurnBonusMode> SelectedBonusMode { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<int> SelectedTurnBonus { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<int> SelectedTopUp { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<NextRunnerMode> SelectedNextRunner { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRandomDestinations))]
    public partial SettingsChoice<DestinationMode> SelectedDestinationMode { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<double> SelectedMinDistance { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<double> SelectedMaxDistance { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<double> SelectedSpacing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRates))]
    public partial SettingsChoice<FareModel> SelectedFareModel { get; set; }

    [ObservableProperty]
    public partial bool DestinationsVisible { get; set; }

    [ObservableProperty]
    public partial string FaresText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DeckText { get; set; } = string.Empty;

    public bool IsFixedBonus => SelectedBonusMode.Value == TurnBonusMode.Fixed;

    public bool IsTopUp => SelectedBonusMode.Value == TurnBonusMode.TopUp;

    public bool IsRandomDestinations => SelectedDestinationMode.Value == DestinationMode.RandomStations;

    public bool HasRates => SelectedFareModel.Value != FareModel.Manual;

    partial void OnSelectedGameDurationChanged(SettingsChoice<TimeSpan> value) => _settings.GameDuration = value.Value;

    partial void OnSelectedHeadStartChanged(SettingsChoice<TimeSpan> value) => _settings.HeadStart = value.Value;

    partial void OnSelectedRevealIntervalChanged(SettingsChoice<TimeSpan> value) => _settings.RevealInterval = value.Value;

    partial void OnSelectedDrawCooldownChanged(SettingsChoice<TimeSpan> value) => _settings.DrawCooldown = value.Value;

    partial void OnSelectedCatchRadiusChanged(SettingsChoice<double> value) => _settings.CatchRadiusMeters = value.Value;

    partial void OnSelectedDestinationRadiusChanged(SettingsChoice<double> value) => _settings.DestinationRadiusMeters = value.Value;

    partial void OnSelectedStartingCoinsChanged(SettingsChoice<int> value) => _settings.StartingCoins = value.Value;

    partial void OnSelectedBonusModeChanged(SettingsChoice<TurnBonusMode> value) => _settings.TurnBonusMode = value.Value;

    partial void OnSelectedTurnBonusChanged(SettingsChoice<int> value) => _settings.TurnBonus = value.Value;

    partial void OnSelectedTopUpChanged(SettingsChoice<int> value) => _settings.TopUpMinimum = value.Value;

    partial void OnSelectedNextRunnerChanged(SettingsChoice<NextRunnerMode> value) => _settings.NextRunner = value.Value;

    partial void OnSelectedDestinationModeChanged(SettingsChoice<DestinationMode> value) => _settings.DestinationMode = value.Value;

    partial void OnSelectedMinDistanceChanged(SettingsChoice<double> value) => _settings.DestinationMinDistanceMeters = value.Value;

    partial void OnSelectedMaxDistanceChanged(SettingsChoice<double> value) => _settings.DestinationMaxDistanceMeters = value.Value;

    partial void OnSelectedSpacingChanged(SettingsChoice<double> value) => _settings.DestinationMinSpacingMeters = value.Value;

    partial void OnDestinationsVisibleChanged(bool value) => _settings.DestinationsVisible = value;

    partial void OnSelectedFareModelChanged(SettingsChoice<FareModel> value)
    {
        _settings.FareModel = value.Value;
        UpdateFares();
    }

    /// <summary>Changes the rate (per km) or fare (per ride) of one means of transport.</summary>
    [RelayCommand]
    private async Task EditFaresAsync()
    {
        var modes = Enum.GetValues<TransportMode>();
        var options = modes.Select(m => $"{TagTexts.Transport(m)}: {Price(m)}").ToList();
        var choice = await _dialogs.ChooseAsync(TagStrings.SettingsFares, options);
        if (choice is null)
        {
            return;
        }

        var mode = modes[options.IndexOf(choice)];
        var perKm = _settings.FareModel == FareModel.PerKm;
        var text = await _dialogs.PromptAsync(
            TagTexts.Transport(mode),
            perKm ? TagStrings.SettingsRatePrompt : TagStrings.SettingsFarePrompt,
            perKm ? FareCalculator.PerKmRate(_settings, mode).ToString(CultureInfo.CurrentCulture) : FareCalculator.FixedFare(_settings, mode).ToString(CultureInfo.CurrentCulture),
            Keyboard.Numeric);
        if (!double.TryParse(text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0 || value > 1000)
        {
            return;
        }

        if (perKm)
        {
            _settings.PerKmRates[mode] = value;
        }
        else
        {
            _settings.FixedFares[mode] = (int)Math.Round(value);
        }

        UpdateFares();
    }

    [RelayCommand]
    private async Task LoadDeckAsync()
    {
        var file = await _files.PickAsync(TagStrings.SettingsDeckLoad);
        if (file is null)
        {
            return;
        }

        try
        {
            CardDeck.Parse(file.Content);
            _settings.CardsJson = file.Content;
            UpdateDeck(file.FileName);
        }
        catch (JsonException ex)
        {
            _logger.LogInformation(ex, "Invalid deck file {File}", file.FileName);
            await _dialogs.AlertAsync(TagStrings.SettingsDeck, string.Format(TagStrings.SettingsDeckInvalidFormat, ex.Message));
        }
    }

    [RelayCommand]
    private void ResetDeck()
    {
        _settings.CardsJson = null;
        UpdateDeck();
    }

    private string Price(TransportMode mode) => _settings.FareModel == FareModel.PerKm
        ? string.Format(TagStrings.PricePerKmFormat, FareCalculator.PerKmRate(_settings, mode))
        : TagTexts.Coins(FareCalculator.FixedFare(_settings, mode));

    private void UpdateFares() =>
        FaresText = _settings.FareModel == FareModel.Manual
            ? TagStrings.SettingsFaresManual
            : string.Join(", ", Enum.GetValues<TransportMode>().Select(m => $"{TagTexts.Transport(m)} {Price(m)}"));

    private void UpdateDeck(string? fileName = null)
    {
        var deck = _settings.LoadCards();
        var summary = string.Format(
            TagStrings.SettingsDeckSummaryFormat,
            deck.Cards.Count(c => c.Type == CardType.Challenge),
            deck.Cards.Count(c => c.Type == CardType.Curse));
        DeckText = _settings.CardsJson is null
            ? string.Format(TagStrings.SettingsDeckBuiltInFormat, summary)
            : string.Format(TagStrings.SettingsDeckCustomFormat, fileName ?? "JSON", summary);
    }

    private static IReadOnlyList<SettingsChoice<TimeSpan>> Minutes(params int[] values) =>
        [.. values.Select(m => new SettingsChoice<TimeSpan>(TimeSpan.FromMinutes(m), TagTexts.Minutes(m)))];

    private static IReadOnlyList<SettingsChoice<double>> Meters(params int[] values) =>
        [.. values.Select(m => new SettingsChoice<double>(m, TagTexts.Distance(m)))];

    private static IReadOnlyList<SettingsChoice<int>> Numbers(params int[] values) =>
        [.. values.Select(n => new SettingsChoice<int>(n, TagTexts.Coins(n)))];

    private static SettingsChoice<T> Closest<T, TDistance>(IReadOnlyList<SettingsChoice<T>> choices, T value, Func<T, T, TDistance> distance)
        where TDistance : IComparable<TDistance> =>
        choices.MinBy(c => distance(c.Value, value))!;
}
