using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using TransitGames.Core.Stations;
using TransitGames.Games.HideAndSeek.Cards;
using TransitGames.Games.HideAndSeek.Maui.Services;
using TransitGames.Games.HideAndSeek.Questions;
using TransitGames.Games.HideAndSeek.Resources;
using TransitGames.Geo.Projection;
using TransitGames.Games.HideAndSeek.Spatial;
using TransitGames.UI.Services;
using TransitGames.UI.ViewModels;

namespace TransitGames.Games.HideAndSeek.Maui.ViewModels;

/// <summary>
/// Main Hide and Seek screen for both roles. Seekers: eliminated/possible area, remaining stations, ask, history,
/// "found". Hiders: hiding countdown, own station and zone, incoming questions, cards, "found". Both see what the
/// seekers know (the eliminated area).
/// </summary>
public sealed partial class HideAndSeekGameViewModel : ObservableObject
{
    private static readonly TimeSpan CurseShownFor = TimeSpan.FromMinutes(30);

    private readonly HideAndSeekController _game;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly ILogger<HideAndSeekGameViewModel> _logger;
    private IDispatcherTimer? _timer;
    private HideAndSeekSession? _observed;
    private int _refreshVersion;

    public HideAndSeekGameViewModel(
        ActiveSessionViewModel header,
        HideAndSeekController game,
        IDialogService dialogs,
        INavigationService navigation,
        ILogger<HideAndSeekGameViewModel> logger)
    {
        Header = header;
        _game = game;
        _dialogs = dialogs;
        _navigation = navigation;
        _logger = logger;
    }

    public ActiveSessionViewModel Header { get; }

    public HideAndSeekSession? Session => _game.Session;

    [ObservableProperty]
    public partial bool IsSeeker { get; set; }

    [ObservableProperty]
    public partial bool IsHider { get; set; }

    [ObservableProperty]
    public partial string PhaseText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>A line needing attention: a question waiting for an answer, a fresh curse, a card to choose.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? NoticeText { get; set; }

    public bool HasNotice => !string.IsNullOrEmpty(NoticeText);

    [ObservableProperty]
    public partial string QuestionsButtonText { get; set; } = HideAndSeekStrings.ButtonQuestions;

    [ObservableProperty]
    public partial string CardsButtonText { get; set; } = HideAndSeekStrings.ButtonCards;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying))]
    public partial bool IsFound { get; set; }

    public bool IsPlaying => !IsFound;

    [ObservableProperty]
    public partial bool CanAsk { get; set; }

    [ObservableProperty]
    public partial bool NeedsStation { get; set; }

    /// <summary>Stations for the map; stations the hiders cannot be at are shown as disabled.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Station> MapStations { get; set; } = [];

    /// <summary>WGS84 geometries for the page's map layers.</summary>
    [ObservableProperty]
    public partial Geometry? EliminatedArea { get; set; }

    [ObservableProperty]
    public partial Geometry? PossibleArea { get; set; }

    [ObservableProperty]
    public partial Geometry? HiderZone { get; set; }

    public void OnAppearing()
    {
        Header.Activate();
        Observe(Session);
        _timer ??= Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
        _ = RefreshAsync();
    }

    public void OnDisappearing()
    {
        Header.Deactivate();
        _timer?.Stop();
        Observe(null);
    }

    private void Observe(HideAndSeekSession? session)
    {
        if (_observed is not null)
        {
            _observed.StateChanged -= OnStateChanged;
        }

        _observed = session;
        if (session is not null)
        {
            session.StateChanged += OnStateChanged;
        }
    }

    private void OnStateChanged(object? sender, EventArgs e) => MainThread.BeginInvokeOnMainThread(() => _ = RefreshAsync());

    private void OnTick(object? sender, EventArgs e) => UpdateTexts();

    private async Task RefreshAsync()
    {
        if (Session is not { } session)
        {
            return;
        }

        IsHider = session.IsHider;
        IsSeeker = !session.IsHider;
        UpdateTexts();

        var version = ++_refreshVersion;
        try
        {
            var result = await _game.ComputeAsync();
            if (result is null || version != _refreshVersion)
            {
                return;
            }

            EliminatedArea = result.EliminatedWgs84;
            PossibleArea = result.PossibleWgs84;
            var possible = result.Stations.Where(s => s.IsPossible).Select(s => s.Station.Id).ToHashSet();
            MapStations = session.Settings.Stations.Select(s => s with { IsEnabled = possible.Contains(s.Id) }).ToList();
            StatusText = string.Format(HideAndSeekStrings.StationsLeftFormat, result.RemainingStations, result.TotalStations);
            HiderZone = HiderZoneGeometry(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Computing the eliminated area failed");
        }
    }

    private Geometry? HiderZoneGeometry(HideAndSeekSession session)
    {
        if (!session.IsHider || _game.Area is not { } area
            || session.Settings.Stations.FirstOrDefault(s => s.Id == session.HiderStationId) is not { } station)
        {
            return null;
        }

        return area.Projection.Unproject(GeoOps.Circle(area.Projection.Project(station.Location), session.Settings.HidingZoneRadiusMeters));
    }

    private void UpdateTexts()
    {
        if (Session is not { } session)
        {
            return;
        }

        var now = _game.Now;
        var phase = session.Phase(now);
        IsFound = phase == HideAndSeekPhase.Found;
        CanAsk = phase == HideAndSeekPhase.Seeking;
        NeedsStation = session.IsHider && session.HiderStationId is null && !IsFound;
        var bonus = session.BonusMinutes > 0 ? " " + string.Format(HideAndSeekStrings.BonusFormat, session.BonusMinutes) : string.Empty;
        PhaseText = phase switch
        {
            HideAndSeekPhase.Hiding => string.Format(
                session.IsHider ? HideAndSeekStrings.PhaseHidingHiderFormat : HideAndSeekStrings.PhaseHidingSeekerFormat,
                Countdown(session.HidingEndsAt - now)),
            HideAndSeekPhase.Seeking => string.Format(
                session.IsHider ? HideAndSeekStrings.PhaseSeekingHiderFormat : HideAndSeekStrings.PhaseSeekingSeekerFormat,
                HideAndSeekSession.FormatDuration(session.HiddenFor(now))) + bonus,
            _ => string.Format(HideAndSeekStrings.PhaseFoundFormat, HideAndSeekSession.FormatDuration(session.HiddenFor(now))) + bonus,
        };

        var unanswered = session.Questions.Where(q => !q.IsAnswered).ToList();
        QuestionsButtonText = unanswered.Count > 0
            ? string.Format(HideAndSeekStrings.ButtonQuestionsNewFormat, session.Questions.Count, unanswered.Count)
            : string.Format(HideAndSeekStrings.ButtonQuestionsFormat, session.Questions.Count);
        CardsButtonText = session.PendingDraws.Count > 0
            ? string.Format(HideAndSeekStrings.ButtonCardsNewFormat, session.Hand.Count)
            : string.Format(HideAndSeekStrings.ButtonCardsFormat, session.Hand.Count);

        NoticeText = session.IsHider ? HiderNotice(session, unanswered, now) : SeekerNotice(session, unanswered, now);
    }

    private static string? HiderNotice(HideAndSeekSession session, List<AskedQuestion> unanswered, DateTimeOffset now)
    {
        if (unanswered.FirstOrDefault() is { } question)
        {
            var left = question.AskedAt + session.Settings.Rule(question.Spec.Type).AnswerTime - now;
            return string.Format(HideAndSeekStrings.NoticeAnswerFormat, QuestionTexts.Title(question.Spec), Countdown(left));
        }

        return session.PendingDraws.Count > 0 ? HideAndSeekStrings.NoticeChooseCards : null;
    }

    private static string? SeekerNotice(HideAndSeekSession session, List<AskedQuestion> unanswered, DateTimeOffset now)
    {
        var curse = session.CardsPlayed.LastOrDefault(p => p.Kind == CardKind.Curse && now - p.PlayedAt < CurseShownFor);
        if (curse is not null)
        {
            var card = session.Deck.Find(curse.CardId);
            return string.Format(HideAndSeekStrings.NoticeCurseFormat, card?.LocalizedTitle, card?.LocalizedText);
        }

        return unanswered.FirstOrDefault() is { } question
            ? string.Format(HideAndSeekStrings.NoticeWaitingFormat, QuestionTexts.Title(question.Spec))
            : null;
    }

    private static string Countdown(TimeSpan left) =>
        left <= TimeSpan.Zero ? HideAndSeekStrings.Overdue : $"{(int)left.TotalMinutes}:{left.Seconds:00}";

    [RelayCommand]
    private Task AskAsync() => _navigation.GoToAsync(HideAndSeekUiModule.AskRoute);

    [RelayCommand]
    private Task QuestionsAsync() => _navigation.GoToAsync(HideAndSeekUiModule.QuestionsRoute);

    [RelayCommand]
    private Task CardsAsync() => _navigation.GoToAsync(HideAndSeekUiModule.CardsRoute);

    [RelayCommand]
    private async Task FoundAsync()
    {
        if (await _dialogs.ConfirmAsync(HideAndSeekStrings.FoundTitle, IsHider ? HideAndSeekStrings.FoundConfirmHider : HideAndSeekStrings.FoundConfirmSeeker))
        {
            await _game.FoundAsync();
        }
    }

    [RelayCommand]
    private async Task RandomStationAsync()
    {
        if (Session is not { } session || session.Settings.Stations.Count == 0)
        {
            return;
        }

        var station = session.Settings.Stations[Random.Shared.Next(session.Settings.Stations.Count)];
        if (await _dialogs.ConfirmAsync(HideAndSeekStrings.StationTitle, string.Format(HideAndSeekStrings.StationRandomFormat, station.Name)))
        {
            session.ChooseStation(station.Id);
        }
    }

    /// <summary>Hiders pick their station by tapping it on the map (only while none is chosen).</summary>
    [RelayCommand]
    private async Task StationTappedAsync(Station station)
    {
        if (Session is not { IsHider: true } session || session.HiderStationId is not null)
        {
            return;
        }

        if (await _dialogs.ConfirmAsync(HideAndSeekStrings.StationTitle, string.Format(HideAndSeekStrings.StationChooseFormat, station.Name)))
        {
            session.ChooseStation(station.Id);
        }
    }
}
