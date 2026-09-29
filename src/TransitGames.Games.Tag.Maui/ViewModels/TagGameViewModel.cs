using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using TransitGames.Core.Geography;
using TransitGames.Games.Tag.Maui.Services;
using TransitGames.Games.Tag.Resources;
using TransitGames.Games.Tag.Targets;
using TransitGames.Geo.Projection;
using TransitGames.UI.Maps;
using TransitGames.UI.Services;
using TransitGames.UI.ViewModels;

namespace TransitGames.Games.Tag.Maui.ViewModels;

/// <summary>
/// Main Tag screen for both roles: targets on the map, round timer, pings (automatic online, by hand offline),
/// "caught", the next round with swapped roles, and the scoreboard. Runners also see the capture range of targets.
/// </summary>
public sealed partial class TagGameViewModel : ObservableObject
{
    private readonly TagController _game;
    private readonly TrackingCoordinator _tracking;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IShare _share;
    private readonly ILogger<TagGameViewModel> _logger;
    private IDispatcherTimer? _timer;
    private TagSession? _observed;
    private (Guid SessionId, int Round, string Role)? _shown;
    private bool _isVisible;
    private bool _announcedGameOver;

    public TagGameViewModel(
        ActiveSessionViewModel header,
        TagController game,
        TrackingCoordinator tracking,
        IDialogService dialogs,
        INavigationService navigation,
        IShare share,
        ILogger<TagGameViewModel> logger)
    {
        Header = header;
        _game = game;
        _tracking = tracking;
        _dialogs = dialogs;
        _navigation = navigation;
        _share = share;
        _logger = logger;
    }

    public ActiveSessionViewModel Header { get; }

    public TagSession? Session => _game.Session;

    [ObservableProperty]
    public partial bool IsRunner { get; set; }

    [ObservableProperty]
    public partial bool IsChaser { get; set; }

    [ObservableProperty]
    public partial string PhaseText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RoundText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? NoticeText { get; set; }

    public bool HasNotice => !string.IsNullOrEmpty(NoticeText);

    [ObservableProperty]
    public partial string TargetsButtonText { get; set; } = TagStrings.ButtonTargets;

    [ObservableProperty]
    public partial string CatchButtonText { get; set; } = TagStrings.ButtonCaught;

    [ObservableProperty]
    public partial bool CanCatch { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial bool IsTimeUp { get; set; }

    [ObservableProperty]
    public partial bool IsGameOver { get; set; }

    [ObservableProperty]
    public partial string NextRoundText { get; set; } = TagStrings.ButtonNextRound;

    /// <summary>Offline: runners send their position by hand, chasers enter it.</summary>
    [ObservableProperty]
    public partial bool ShowPing { get; set; }

    [ObservableProperty]
    public partial string PingButtonText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowCoins { get; set; }

    [ObservableProperty]
    public partial bool HasExtraRow { get; set; }

    /// <summary>Chasers tapping the map to enter the runners' position.</summary>
    [ObservableProperty]
    public partial bool IsPickingPing { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<MapMarker> Markers { get; set; } = [];

    /// <summary>Capture range of the active targets (runners), WGS84.</summary>
    [ObservableProperty]
    public partial Geometry? TargetZones { get; set; }

    public void OnAppearing()
    {
        _isVisible = true;
        Header.Activate();
        Observe(Session);
        _game.ChallengeInReach -= OnChallengeInReach;
        _game.ChallengeInReach += OnChallengeInReach;
        _timer ??= Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
        Refresh();
    }

    public void OnDisappearing()
    {
        _isVisible = false;
        Header.Deactivate();
        _timer?.Stop();
        _game.ChallengeInReach -= OnChallengeInReach;
        Observe(null);
        IsPickingPing = false;
    }

    private void Observe(TagSession? session)
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

    private void OnStateChanged(object? sender, EventArgs e) => MainThread.BeginInvokeOnMainThread(Refresh);

    private void OnTick(object? sender, EventArgs e) => UpdateTexts();

    private void Refresh()
    {
        if (Session is not { } session)
        {
            return;
        }

        IsRunner = session.IsRunner;
        IsChaser = !session.IsRunner;
        Header.RefreshRole();
        UpdateTexts();
        UpdateMap(session);
        _ = OnRoundOrRoleChangedAsync(session);
    }

    /// <summary>Tells the players when a new round starts with swapped roles; runners need GPS fixes for the geofence.</summary>
    private async Task OnRoundOrRoleChangedAsync(TagSession session)
    {
        var current = (session.Id, session.CurrentRound.Index, session.LocalRoleId);
        var previous = _shown;
        _shown = current;
        try
        {
            if (session.IsRunner && session.Settings.AutoCapture && session.Phase(_game.Now) != TagPhase.GameOver)
            {
                await _tracking.EnsureRecordingAsync(session);
            }

            // A round end arrives as two changes (the round, then the roles): announce once, when it is complete.
            var gameOver = session.Phase(_game.Now) == TagPhase.GameOver;
            if (previous is not { } before || before.SessionId != session.Id || !_isVisible
                || (before.Role == current.LocalRoleId && !(gameOver && before.Round == current.Index)))
            {
                return;
            }

            if (before.Role == current.LocalRoleId && _announcedGameOver)
            {
                return;
            }

            _announcedGameOver = gameOver;
            var ended = session.Rounds.LastOrDefault(r => r.EndedAt is not null);
            var summary = ended is null ? string.Empty : string.Format(
                TagStrings.RoundEndedFormat,
                TagTexts.TeamName(ended.Runners, session.LocalTeam),
                TagTexts.EndReason(ended.EndReason),
                TagTexts.Duration((ended.EndedAt ?? _game.Now) - ended.StartedAt));
            var next = gameOver
                ? TagStrings.RoundEndedGameOver
                : session.IsRunner ? TagStrings.RoundEndedNowRunners : string.Format(TagStrings.RoundEndedNowChasersFormat, TagTexts.Duration(session.Settings.HeadStart));
            await _dialogs.AlertAsync(TagStrings.RoundEndedTitle, summary + "\n\n" + next);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Announcing the new round failed");
        }
    }

    private void UpdateTexts()
    {
        if (Session is not { } session)
        {
            return;
        }

        var now = _game.Now;
        var phase = session.Phase(now);
        var round = session.CurrentRound;
        IsPlaying = phase is TagPhase.HeadStart or TagPhase.Chase;
        IsTimeUp = phase == TagPhase.TimeUp;
        IsGameOver = phase == TagPhase.GameOver;
        CanCatch = session.CanEndByCatch(now);
        CatchButtonText = session.IsRunner ? TagStrings.ButtonCaughtRunner : TagStrings.ButtonCaught;
        NextRoundText = session.IsLastRound ? TagStrings.ButtonEndRound : TagStrings.ButtonNextRound;
        ShowPing = !_game.IsOnline && IsPlaying;
        PingButtonText = session.IsRunner ? TagStrings.ButtonSendPosition : IsPickingPing ? TagStrings.ButtonPingCancel : TagStrings.ButtonAddPing;
        ShowCoins = session.IsRunner && session.Settings.RewardKind == RewardKind.Coins && IsPlaying;
        HasExtraRow = ShowPing || ShowCoins;

        RoundText = string.Format(
            TagStrings.RoundFormat,
            round.Index + 1,
            session.Settings.Rounds,
            TagTexts.TeamName(round.Runners, session.LocalTeam));
        PhaseText = phase switch
        {
            TagPhase.HeadStart => string.Format(
                session.IsRunner ? TagStrings.PhaseHeadStartRunnerFormat : TagStrings.PhaseHeadStartChaserFormat,
                TagTexts.Duration(session.HeadStartEndsAt - now)),
            TagPhase.Chase => string.Format(
                session.IsRunner ? TagStrings.PhaseChaseRunnerFormat : TagStrings.PhaseChaseChaserFormat,
                TagTexts.Duration(now - round.StartedAt),
                TagTexts.Duration(session.RoundEndsAt - now)),
            TagPhase.TimeUp => TagStrings.PhaseTimeUp,
            _ => TagStrings.PhaseGameOver,
        };

        var captured = round.Captures.Count;
        TargetsButtonText = string.Format(TagStrings.ButtonTargetsFormat, captured, session.Settings.Targets.Count);
        StatusText = Status(session, now);
        NoticeText = Notice(session, phase, now);
    }

    private string Status(TagSession session, DateTimeOffset now)
    {
        var round = session.CurrentRound;
        var parts = new List<string>();
        if (session.IsRunner)
        {
            parts.Add(session.Settings.RewardKind == RewardKind.Coins
                ? string.Format(TagStrings.StatusCoinsFormat, session.CoinBalance)
                : string.Format(TagStrings.StatusPointsFormat, round.Captures.Sum(c => c.Reward)));
            if (!_game.IsOnline && session.NextRevealAt(now) is { } next)
            {
                parts.Add(string.Format(TagStrings.StatusNextPingFormat, next.ToLocalTime(), TagTexts.Duration(next - now)));
            }
        }
        else if (!_game.IsOnline)
        {
            parts.Add(session.LastPing is { } ping
                ? string.Format(TagStrings.StatusLastPingFormat, ping.At.ToLocalTime())
                : TagStrings.StatusNoPing);
        }

        parts.Add(string.Format(TagStrings.StatusCatchRadiusFormat, TagTexts.Distance(session.Settings.CatchRadiusMeters)));
        return string.Join(" · ", parts);
    }

    private string? Notice(TagSession session, TagPhase phase, DateTimeOffset now)
    {
        if (IsPickingPing)
        {
            return TagStrings.NoticePickPing;
        }

        switch (phase)
        {
            case TagPhase.TimeUp:
                return TagStrings.NoticeTimeUp;
            case TagPhase.GameOver:
                var leader = session.Scoreboard(now).Leader;
                return leader is { } team
                    ? string.Format(TagStrings.NoticeWinnerFormat, TagTexts.TeamName(team, session.LocalTeam))
                    : TagStrings.NoticeDraw;
        }

        if (!_game.IsOnline && PingDue(session, now))
        {
            return session.IsRunner ? TagStrings.NoticePingDueRunner : TagStrings.NoticePingDueChaser;
        }

        return null;
    }

    /// <summary>Offline: a reveal time passed since the last position was sent (runners) or entered (chasers).</summary>
    private static bool PingDue(TagSession session, DateTimeOffset now)
    {
        if (session.NextRevealAt(now) is not { } next)
        {
            return false;
        }

        var due = next - session.Settings.RevealInterval;
        if (due <= session.CurrentRound.StartedAt)
        {
            return false;
        }

        var last = session.IsRunner ? session.LastOwnPingAt : session.LastPing?.At;
        return last is null || last < due;
    }

    private void UpdateMap(TagSession session)
    {
        var markers = new List<MapMarker>();
        foreach (var target in session.Settings.Targets)
        {
            var captured = session.IsCaptured(target.Id);
            markers.Add(new MapMarker(
                target.Location,
                captured ? MapPalette.TargetCaptured : MapPalette.Target,
                captured ? target.Name : $"{target.Name} · {TagTexts.Reward(target.Reward, session.Settings.RewardKind)}",
                0.55));
        }

        if (session.Settings.FinalTarget is { } final)
        {
            markers.Add(new MapMarker(final.Location, MapPalette.FinalTarget, final.Name, 0.7));
        }

        if (!session.IsRunner && session.LastPing is { } ping)
        {
            markers.Add(new MapMarker(ping.Point, MapPalette.Opponent, string.Format(TagStrings.MapPingFormat, ping.At.ToLocalTime()), 0.6));
        }

        Markers = markers;
        TargetZones = session.IsRunner ? Zones(session) : null;
    }

    private Geometry? Zones(TagSession session)
    {
        if (_game.Area is not { } area)
        {
            return null;
        }

        var circles = session.ActiveTargets
            .Select(t => (Geometry)area.Projection.ProjectPoint(t.Location).Buffer(session.Settings.TargetRadiusMeters, 16))
            .ToArray();
        return circles.Length == 0 ? null : area.Projection.Unproject(GeometryFactories.Local.BuildGeometry(circles));
    }

    private void OnChallengeInReach(object? sender, TagTarget target) =>
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (_isVisible && target.Challenge is { } challenge && Session?.CanCapture(target.Id, _game.Now) == true
                    && await _dialogs.ConfirmAsync(
                        string.Format(TagStrings.ChallengeInReachTitleFormat, target.Name),
                        $"{challenge.LocalizedTitle}\n\n{challenge.LocalizedText}\n\n{TagStrings.ChallengeDoneQuestion}",
                        TagStrings.ChallengeDone,
                        TagStrings.ChallengeLater))
                {
                    await _game.CaptureAsync(target.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Confirming the challenge failed");
            }
        });

    [RelayCommand]
    private Task TargetsAsync() => _navigation.GoToAsync(TagUiModule.TargetsRoute);

    [RelayCommand]
    private Task ScoreboardAsync() => _navigation.GoToAsync(TagUiModule.ScoreboardRoute);

    [RelayCommand]
    private async Task CaughtAsync()
    {
        if (Session is not { } session || !session.CanEndByCatch(_game.Now))
        {
            return;
        }

        var message = session.IsLastRound
            ? TagStrings.CaughtConfirmLast
            : session.IsRunner ? TagStrings.CaughtConfirmRunner : TagStrings.CaughtConfirmChaser;
        if (await _dialogs.ConfirmAsync(TagStrings.CaughtTitle, message))
        {
            await _game.EndRoundAsync(RoundEndReason.Caught);
        }
    }

    [RelayCommand]
    private async Task NextRoundAsync()
    {
        if (Session is not { } session || session.Phase(_game.Now) != TagPhase.TimeUp)
        {
            return;
        }

        if (await _dialogs.ConfirmAsync(TagStrings.TimeUpTitle, session.IsLastRound ? TagStrings.TimeUpConfirmLast : TagStrings.TimeUpConfirmNext))
        {
            await _game.EndRoundAsync(RoundEndReason.TimeUp);
        }
    }

    [RelayCommand]
    private async Task PingAsync()
    {
        if (Session is not { } session)
        {
            return;
        }

        if (!session.IsRunner)
        {
            IsPickingPing = !IsPickingPing;
            UpdateTexts();
            return;
        }

        var here = await _game.CurrentLocationAsync();
        if (here is not { } point)
        {
            await _dialogs.AlertAsync(TagStrings.PingTitle, TagStrings.PingNoLocation);
            return;
        }

        var now = _game.Now;
        await _share.RequestAsync(new ShareTextRequest
        {
            Title = TagStrings.PingTitle,
            Text = string.Format(
                TagStrings.PingShareTextFormat,
                now.ToLocalTime(),
                point,
                FormattableString.Invariant($"https://www.openstreetmap.org/?mlat={point.Latitude:F5}&mlon={point.Longitude:F5}#map=17/{point.Latitude:F5}/{point.Longitude:F5}")),
        });
        session.MarkOwnPing(now);
    }

    /// <summary>Chasers entering the runners' position: a tap on the map.</summary>
    [RelayCommand]
    private void MapTapped(GeoPoint point)
    {
        if (!IsPickingPing || Session is not { IsRunner: false } session)
        {
            return;
        }

        IsPickingPing = false;
        session.AddPing(point, _game.Now);
    }

    [RelayCommand]
    private async Task SpendAsync()
    {
        if (Session is not { } session)
        {
            return;
        }

        var amountText = await _dialogs.PromptAsync(
            TagStrings.SpendTitle,
            string.Format(TagStrings.SpendAmountFormat, session.CoinBalance),
            keyboard: Keyboard.Numeric);
        if (!int.TryParse(amountText, out var amount) || amount <= 0)
        {
            return;
        }

        if (amount > session.CoinBalance)
        {
            await _dialogs.AlertAsync(TagStrings.SpendTitle, TagStrings.SpendTooMuch);
            return;
        }

        var note = await _dialogs.PromptAsync(TagStrings.SpendTitle, TagStrings.SpendNote) ?? string.Empty;
        await _game.SpendAsync(amount, note.Trim());
    }
}
