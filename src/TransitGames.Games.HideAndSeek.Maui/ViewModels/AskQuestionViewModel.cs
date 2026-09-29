using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TransitGames.Core.Geography;
using TransitGames.Geo.Areas;
using TransitGames.Geo.Overpass;
using TransitGames.Games.HideAndSeek.Maui.Services;
using TransitGames.Geo.Places;
using TransitGames.Games.HideAndSeek.Questions;
using TransitGames.Games.HideAndSeek.Resources;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;

namespace TransitGames.Games.HideAndSeek.Maui.ViewModels;

public sealed partial class QuestionTypeChoice(QuestionType type) : ObservableObject
{
    public QuestionType Type { get; } = type;

    public string Name { get; } = QuestionTexts.TypeName(type);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// Asking a question. The position is this device's GPS and can be corrected by tapping the map. The same screen
/// lets offline hiders enter a question they received by message (then the position is where the seekers said
/// they are).
/// </summary>
public sealed partial class AskQuestionViewModel : ObservableObject
{
    private static readonly double[] RadarRadii = [500, 1000, 2000, 3000, 5000, 10000, 15000];
    private static readonly double[] TentacleRadii = [1000, 2000, 5000, 10000];

    private readonly HideAndSeekController _game;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly ILogger<AskQuestionViewModel> _logger;

    public AskQuestionViewModel(HideAndSeekController game, IDialogService dialogs, INavigationService navigation, ILogger<AskQuestionViewModel> logger)
    {
        _game = game;
        _dialogs = dialogs;
        _navigation = navigation;
        _logger = logger;
    }

    public ObservableCollection<QuestionTypeChoice> Types { get; } = [];

    public ObservableCollection<SettingsChoice<double>> Radii { get; } = [];

    public ObservableCollection<SettingsChoice<string>> Categories { get; } = [];

    [ObservableProperty]
    public partial GameArea? Area { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRadius), nameof(HasCategory), nameof(IsThermometer), nameof(IsPhoto), nameof(RuleText))]
    public partial QuestionType? SelectedType { get; set; }

    public bool HasRadius => SelectedType is QuestionType.Radar or QuestionType.Tentacles;

    public bool HasCategory => SelectedType is QuestionType.Matching or QuestionType.Measuring or QuestionType.Tentacles;

    public bool IsThermometer => SelectedType == QuestionType.Thermometer;

    public bool IsPhoto => SelectedType == QuestionType.Photo;

    public string RuleText => SelectedType is { } type && _game.Session is { } session
        ? string.Format(HideAndSeekStrings.RuleFormat, session.Settings.Rule(type).CardsDrawn, session.Settings.Rule(type).CardsKept, (int)session.Settings.Rule(type).AnswerTime.TotalMinutes)
        : string.Empty;

    [ObservableProperty]
    public partial SettingsChoice<double>? SelectedRadius { get; set; }

    [ObservableProperty]
    public partial SettingsChoice<string>? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial string PhotoSubject { get; set; } = string.Empty;

    /// <summary>The seekers' position used for the question.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocationText))]
    public partial GeoPoint? Location { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocationText))]
    public partial bool IsLocationCorrected { get; set; }

    public string LocationText => IsEnteredByHiders
        ? (Location is null ? HideAndSeekStrings.AskTapSeekers : HideAndSeekStrings.AskLocationCorrected)
        : Location is null ? HideAndSeekStrings.AskNoLocation
        : IsLocationCorrected ? HideAndSeekStrings.AskLocationCorrected : HideAndSeekStrings.AskLocationGps;

    /// <summary>Offline hiders entering a received question: the position is the seekers', tapped on the map.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocationText), nameof(ButtonText))]
    public partial bool IsEnteredByHiders { get; set; }

    public string ButtonText => IsEnteredByHiders ? HideAndSeekStrings.AddQuestionButton : HideAndSeekStrings.AskButton;

    [ObservableProperty]
    public partial string ThermometerText { get; set; } = string.Empty;

    /// <summary>Thermometer start, drawn on the map.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<GeoPoint> MapMarks { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    public partial string? BusyText { get; set; }

    public bool IsBusy => BusyText is not null;

    public async Task OnAppearingAsync()
    {
        if (_game.Session is not { } session)
        {
            return;
        }

        Area = _game.Area;
        if (Types.Count == 0)
        {
            foreach (var type in Enum.GetValues<QuestionType>().Where(t => session.Settings.Rule(t).Enabled))
            {
                Types.Add(new QuestionTypeChoice(type));
            }

            if (Types.FirstOrDefault() is { } first)
            {
                SelectType(first);
            }
        }

        IsEnteredByHiders = session.IsHider;
        UpdateThermometer();
        if (!IsLocationCorrected && !IsEnteredByHiders)
        {
            await UseGpsAsync();
        }
    }

    [RelayCommand]
    private void SelectType(QuestionTypeChoice choice)
    {
        foreach (var type in Types)
        {
            type.IsSelected = type == choice;
        }

        SelectedType = choice.Type;
        Radii.Clear();
        foreach (var radius in choice.Type == QuestionType.Tentacles ? TentacleRadii : RadarRadii)
        {
            Radii.Add(new SettingsChoice<double>(radius, QuestionTexts.Distance(radius)));
        }

        SelectedRadius = Radii.FirstOrDefault(r => r.Value == (choice.Type == QuestionType.Tentacles ? 2000 : 1000));

        Categories.Clear();
        foreach (var category in PlaceCategories.All.Where(c => choice.Type == QuestionType.Matching || c.Shape == PlaceShape.Point))
        {
            Categories.Add(new SettingsChoice<string>(category.Id, QuestionTexts.CategoryName(category.Id)));
        }

        SelectedCategory = Categories.FirstOrDefault();
    }

    [RelayCommand]
    private async Task UseGpsAsync()
    {
        BusyText = HideAndSeekStrings.AskLocating;
        try
        {
            var location = await _game.CurrentLocationAsync();
            if (location is not null)
            {
                Location = location;
                IsLocationCorrected = false;
            }
        }
        finally
        {
            BusyText = null;
        }
    }

    /// <summary>Tapping the map corrects the position (bad GPS in a tunnel, or offline hiders entering it).</summary>
    [RelayCommand]
    private void MapTapped(GeoPoint point)
    {
        Location = point;
        IsLocationCorrected = true;
    }

    [RelayCommand]
    private void SetThermometerStart()
    {
        if (_game.Session is { } session && Location is { } here)
        {
            session.SetThermometerStart(here);
            UpdateThermometer();
        }
    }

    private void UpdateThermometer()
    {
        var start = _game.Session?.ThermometerStart;
        MapMarks = start is { } s ? [s] : [];
        ThermometerText = start is null
            ? HideAndSeekStrings.ThermometerNoStart
            : string.Format(HideAndSeekStrings.ThermometerStartFormat, Location is { } here && _game.Area is { } area
                ? QuestionTexts.Distance(area.Projection.Distance(start.Value, here))
                : "?");
    }

    partial void OnLocationChanged(GeoPoint? value) => UpdateThermometer();

    private bool CanAsk() => BusyText is null;

    [RelayCommand(CanExecute = nameof(CanAsk))]
    private async Task AskAsync()
    {
        if (SelectedType is not { } type || _game.Session is not { } session)
        {
            return;
        }

        if (Location is not { } location && type != QuestionType.Photo)
        {
            await _dialogs.AlertAsync(HideAndSeekStrings.AskTitle, HideAndSeekStrings.AskNoLocation);
            return;
        }

        try
        {
            BusyText = HideAndSeekStrings.AskPreparing;
            var spec = await BuildAsync(type, Location ?? default, session);
            if (spec is null)
            {
                return;
            }

            if (!await _dialogs.ConfirmAsync(QuestionTexts.Title(spec), QuestionTexts.Describe(spec), HideAndSeekStrings.AskConfirm, UiStrings.Cancel))
            {
                return;
            }

            await _game.AskAsync(spec);
            await _navigation.GoBackAsync();
        }
        catch (OverpassException ex)
        {
            _logger.LogWarning(ex, "Downloading places failed");
            await _dialogs.AlertAsync(UiStrings.Error, string.Format(UiStrings.NetworkErrorFormat, ex.Message));
        }
        finally
        {
            BusyText = null;
        }
    }

    private async Task<QuestionSpec?> BuildAsync(QuestionType type, GeoPoint location, HideAndSeekSession session)
    {
        switch (type)
        {
            case QuestionType.Radar:
                return new RadarSpec(location, SelectedRadius?.Value ?? 1000);
            case QuestionType.Thermometer:
                if (session.ThermometerStart is not { } start || _game.Area is not { } area || area.Projection.Distance(start, location) < 50)
                {
                    await _dialogs.AlertAsync(HideAndSeekStrings.AskTitle, HideAndSeekStrings.ThermometerTooShort);
                    return null;
                }

                return new ThermometerSpec(start, location);
            case QuestionType.Photo:
                if (string.IsNullOrWhiteSpace(PhotoSubject))
                {
                    await _dialogs.AlertAsync(HideAndSeekStrings.AskTitle, HideAndSeekStrings.PhotoSubjectMissing);
                    return null;
                }

                return new PhotoSpec(PhotoSubject.Trim());
        }

        var categoryId = SelectedCategory?.Value ?? PlaceCategories.StationsId;
        BusyText = string.Format(HideAndSeekStrings.AskDownloadingFormat, QuestionTexts.CategoryName(categoryId));
        var places = await _game.PlacesAsync(categoryId);
        if (places.Count == 0)
        {
            await _dialogs.AlertAsync(HideAndSeekStrings.AskTitle, string.Format(HideAndSeekStrings.NoPlacesFormat, QuestionTexts.CategoryName(categoryId)));
            return null;
        }

        QuestionSpec spec = type switch
        {
            QuestionType.Matching => new MatchingSpec(location, categoryId, places),
            QuestionType.Measuring => new MeasuringSpec(location, categoryId, places),
            _ => new TentaclesSpec(location, categoryId, SelectedRadius?.Value ?? 2000, places),
        };

        if (spec is MatchingSpec matching && QuestionTexts.SeekersPlace(matching) is null)
        {
            await _dialogs.AlertAsync(HideAndSeekStrings.AskTitle, HideAndSeekStrings.NoDistrictHere);
            return null;
        }

        if (spec is TentaclesSpec tentacles && QuestionTexts.Options(tentacles).Count <= 1)
        {
            await _dialogs.AlertAsync(HideAndSeekStrings.AskTitle, string.Format(HideAndSeekStrings.NoPlacesInRadiusFormat, QuestionTexts.CategoryName(categoryId), QuestionTexts.Distance(tentacles.RadiusMeters)));
            return null;
        }

        return spec;
    }
}
