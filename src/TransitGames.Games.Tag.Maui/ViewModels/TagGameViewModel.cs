using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TransitGames.Core.Geography;
using TransitGames.Games.Tag.Cards;
using TransitGames.Games.Tag.Coins;
using TransitGames.Games.Tag.Destinations;
using TransitGames.Games.Tag.Maui.Services;
using TransitGames.Games.Tag.Resources;
using TransitGames.UI.Maps;
using TransitGames.UI.Services;
using TransitGames.UI.ViewModels;

namespace TransitGames.Games.Tag.Maui.ViewModels;

/// <summary>A line of the info panel (coins, ride, challenge, curse, player...).</summary>
public sealed record InfoRow(string Icon, string Title, string Detail);

/// <summary>A button of the action panel; the set depends on the role and the phase.</summary>
public sealed record TagAction(string Text, ICommand Command, bool IsPrimary = false, bool IsEnabled = true)
{
    public bool IsSecondary => !IsPrimary;
}

/// <summary>
/// Main Tag screen for every player and phase: setup (players, destinations, start point, start), the runner's panel
/// (coins, rides, cards, curses, catch, arrival), the chasers' panel (head start lock, catch request) and the host's
/// extras (forced catch, coin correction, settling the end). Everything else comes from the session state.
/// </summary>
public sealed partial class TagGameViewModel : ObservableObject
{
    private readonly TagController _game;
    private readonly TrackingCoordinator _tracking;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly ILogger<TagGameViewModel> _logger;
    private IDispatcherTimer? _timer;
    private TagSession? _observed;
    private string _actionsKey = string.Empty;
    private (Guid SessionId, int Turn)? _announcedTurn;
    private bool _announcedEnd;
    private bool _isVisible;

    public TagGameViewModel(
        ActiveSessionViewModel header,
        TagController game,
        TrackingCoordinator tracking,
        IDialogService dialogs,
        INavigationService navigation,
        ILogger<TagGameViewModel> logger)
    {
        Header = header;
        _game = game;
        _tracking = tracking;
        _dialogs = dialogs;
        _navigation = navigation;
        _logger = logger;
    }

    public ActiveSessionViewModel Header { get; }

    public TagSession? Session => _game.Session;

    [ObservableProperty]
    public partial string PhaseTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PhaseDetail { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? NoticeText { get; set; }

    public bool HasNotice => !string.IsNullOrEmpty(NoticeText);

    [ObservableProperty]
    public partial IReadOnlyList<InfoRow> Info { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<TagAction> Actions { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<MapMarker> Markers { get; set; } = [];

    public void OnAppearing()
    {
        _isVisible = true;
        Header.Activate();
        Observe(Session);
        _game.DestinationInReach -= OnDestinationInReach;
        _game.DestinationInReach += OnDestinationInReach;
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
        _game.DestinationInReach -= OnDestinationInReach;
        Observe(null);
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

    private void OnTick(object? sender, EventArgs e)
    {
        UpdatePanel();
        if (Session is { } session && session.Phase(_game.Now) == TagPhase.TimeUp && session.IsLocalRunner)
        {
            _ = _game.SettleTimeUpAsync();
        }
    }

    private void Refresh()
    {
        if (Session is not { } session)
        {
            return;
        }

        Header.RefreshRole();
        UpdatePanel();
        UpdateMap(session);
        _ = AnnounceAsync(session);
    }

    // ---- Panel -----------------------------------------------------------------------------------------------

    private void UpdatePanel()
    {
        if (Session is not { } session)
        {
            return;
        }

        var now = _game.Now;
        var phase = session.Phase(now);
        (PhaseTitle, PhaseDetail) = PhaseTexts(session, phase, now);
        NoticeText = Notice(session, phase, now);

        var info = BuildInfo(session, phase, now);
        if (!info.SequenceEqual(Info))
        {
            Info = info;
        }

        // Buttons are rebuilt only when what they offer changes (not every second).
        var key = ActionsKey(session, phase, now);
        if (key != _actionsKey)
        {
            _actionsKey = key;
            Actions = BuildActions(session, phase, now);
        }
    }

    private (string Title, string Detail) PhaseTexts(TagSession session, TagPhase phase, DateTimeOffset now)
    {
        var turn = session.CurrentTurn;
        var left = session.GameEndsAt is { } end ? TagTexts.Duration(end - now) : string.Empty;
        return phase switch
        {
            TagPhase.Setup => (TagStrings.PhaseSetup, SetupDetail(session)),
            TagPhase.HeadStart or TagPhase.Chase when !session.IsLocalPlayerInGame => (TagStrings.PhaseChase, TagStrings.SetupNotInGame),
            TagPhase.HeadStart when session.IsLocalRunner => (
                TagStrings.PhaseRunnerHeadStart,
                string.Format(TagStrings.HeadStartLeftFormat, TagTexts.Duration(session.HeadStartEndsAt!.Value - now), left)),
            TagPhase.HeadStart => (
                string.Format(TagStrings.PhaseChaseLockedFormat, TagTexts.Duration(session.HeadStartEndsAt!.Value - now)),
                string.Format(TagStrings.RunnerIsFormat, session.Runner?.Name, left)),
            TagPhase.Chase when session.IsLocalRunner => (
                TagStrings.PhaseRunnerChase,
                string.Format(TagStrings.TurnTimeFormat, TagTexts.Duration(now - turn!.StartedAt), left)),
            TagPhase.Chase => (
                TagStrings.PhaseChase,
                string.Format(TagStrings.RunnerIsFormat, session.Runner?.Name, left)),
            TagPhase.TimeUp => (TagStrings.PhaseTimeUp, session.IsLocalRunner ? TagStrings.TimeUpRunnerDetail : TagStrings.TimeUpWaitDetail),
            _ => (TagStrings.PhaseGameOver, TagTexts.Headline(session.Results(now))),
        };
    }

    private string SetupDetail(TagSession session)
    {
        var mode = session.Settings.DestinationMode;
        return session.IsHost
            ? mode == DestinationMode.RandomStations ? TagStrings.SetupHostRandom : TagStrings.SetupHost
            : mode == DestinationMode.SelfPick ? TagStrings.SetupGuestPick : TagStrings.SetupGuestWait;
    }

    private string? Notice(TagSession session, TagPhase phase, DateTimeOffset now)
    {
        if (phase == TagPhase.Setup && session.IsHost && _game.Peers.Count < 2)
        {
            return TagStrings.NoticeWaitingForPlayers;
        }

        if (session.IsLocalRunner && session.PendingCatchRequests.FirstOrDefault() is { } request)
        {
            return string.Format(TagStrings.NoticeCatchRequestFormat, session.NameOf(request.ChaserId));
        }

        if (phase == TagPhase.TimeUp && session.IsHost && !session.IsLocalRunner)
        {
            return TagStrings.NoticeHostSettle;
        }

        return null;
    }

    private List<InfoRow> BuildInfo(TagSession session, TagPhase phase, DateTimeOffset now)
    {
        var rows = new List<InfoRow>();
        if (phase == TagPhase.Setup)
        {
            foreach (var peer in _game.Peers)
            {
                var destination = session.PendingDestinations.GetValueOrDefault(peer.DeviceId);
                var visible = destination is not null && (session.Settings.DestinationsVisible || peer.DeviceId == session.LocalPlayerId);
                rows.Add(new InfoRow(
                    peer.IsConnected ? "🟢" : "⚪",
                    peer.DeviceId == session.LocalPlayerId ? string.Format(TagStrings.YouFormat, peer.Name) : peer.Name,
                    session.Settings.DestinationMode == DestinationMode.RandomStations ? TagStrings.DestinationDrawnAtStart
                    : destination is null ? TagStrings.DestinationMissing
                    : visible ? destination.Name
                    : TagStrings.DestinationHidden));
            }

            return rows;
        }

        var me = session.LocalPlayerId;
        if (session.LocalPlayer is { } local)
        {
            rows.Add(new InfoRow("🪙", TagStrings.InfoBalance, TagTexts.Coins(session.Balance(me))));
            if (local.Destination is { } destination)
            {
                var distance = _game.LastLocation is { } here ? " · " + TagTexts.Distance(here.DistanceTo(destination.Location)) : string.Empty;
                rows.Add(new InfoRow("🎯", TagStrings.InfoOwnDestination, destination.Name + distance));
            }
        }

        if (phase is TagPhase.HeadStart or TagPhase.Chase)
        {
            if (session.IsLocalRunner)
            {
                AddRunnerRows(session, rows, now);
            }
            else if (session.Runner is { } runner)
            {
                rows.Add(new InfoRow("🏃", string.Format(TagStrings.InfoRunnerFormat, runner.Name), TagTexts.Coins(session.Balance(runner.Id))));
                if (session.OpenRide(runner.Id) is { } ride)
                {
                    rows.Add(new InfoRow("🚌", TagStrings.InfoRunnerRiding, TagTexts.Transport(ride.Mode)));
                }

                foreach (var curse in session.ActiveCurses(runner.Id, now))
                {
                    rows.Add(CurseRow(curse));
                }

                if (_game.LastRunnerSighting is { } sighting)
                {
                    rows.Add(new InfoRow("📍", TagStrings.InfoLastPing, sighting.At.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)));
                }
            }
        }

        return rows;
    }

    private void AddRunnerRows(TagSession session, List<InfoRow> rows, DateTimeOffset now)
    {
        var me = session.LocalPlayerId;
        if (session.OpenRide(me) is { } ride)
        {
            rows.Add(new InfoRow(
                "🚌",
                string.Format(TagStrings.InfoRideFormat, TagTexts.Transport(ride.Mode)),
                string.Format(TagStrings.InfoRideDetailFormat, ride.StartedAt.ToLocalTime(), ride.Upfront)));
        }

        if (session.ActiveChallenge(now) is { } challenge && session.CardOf(challenge) is { } card)
        {
            var deadline = session.ChallengeDeadline(challenge) is { } until
                ? " · " + string.Format(TagStrings.UntilFormat, until.ToLocalTime())
                : string.Empty;
            rows.Add(new InfoRow("📋", $"{card.LocalizedTitle} (+{card.Reward})", card.LocalizedDescription + deadline));
        }

        foreach (var curse in session.ActiveCurses(me, now))
        {
            rows.Add(CurseRow(curse));
        }

        var block = session.DrawBlockReason(now);
        if (block == DrawBlock.Cooldown && session.DrawCooldownEndsAt(now) is { } cooldown)
        {
            rows.Add(new InfoRow("⏳", TagStrings.InfoCooldown, string.Format(TagStrings.UntilFormat, cooldown.ToLocalTime())));
        }
    }

    private static InfoRow CurseRow(ActiveCurse curse)
    {
        var until = curse.ExpiresAt is { } at && curse.Card.CurseDuration.Kind == CurseDurationKind.Minutes
            ? string.Format(TagStrings.UntilFormat, at.ToLocalTime())
            : TagTexts.CurseDuration(curse.Card.CurseDuration);
        var restriction = TagTexts.Restriction(curse.Card.Restriction) ?? TagStrings.HonourRule;
        return new InfoRow("🧿", curse.Card.LocalizedTitle, $"{curse.Card.LocalizedDescription}\n{restriction} · {until}");
    }

    private string ActionsKey(TagSession session, TagPhase phase, DateTimeOffset now) => string.Join('|',
        phase,
        session.IsLocalRunner,
        session.IsHost,
        session.RolesVersion,
        session.CurrentTurn?.Index,
        session.OpenRide(session.LocalPlayerId)?.Id,
        session.DrawBlockReason(now),
        session.ActiveChallenge(now)?.Id,
        session.CanRequestCatch(now),
        session.CanConfirmCatch(now),
        _game.LastLocation is { } here && session.CanReportArrival(here, now),
        session.PendingDestinations.Count,
        _game.Peers.Count);

    private List<TagAction> BuildActions(TagSession session, TagPhase phase, DateTimeOffset now)
    {
        var actions = new List<TagAction>();
        switch (phase)
        {
            case TagPhase.Setup:
                if (session.CanSetDestination(session.LocalPlayerId) && session.Settings.DestinationMode == DestinationMode.SelfPick)
                {
                    actions.Add(new(TagStrings.ActionPickDestination, PickOwnDestinationCommand, IsPrimary: !session.IsHost));
                }

                if (session.IsHost && session.Settings.DestinationMode == DestinationMode.HostSets)
                {
                    actions.Add(new(TagStrings.ActionSetDestinations, SetPlayerDestinationCommand));
                }

                if (session.IsHost)
                {
                    actions.Add(new(TagStrings.ActionStart, StartGameCommand, IsPrimary: true, IsEnabled: _game.Peers.Count >= 2));
                }

                break;

            case TagPhase.HeadStart or TagPhase.Chase when session.IsLocalRunner:
                var ride = session.OpenRide(session.LocalPlayerId);
                actions.Add(new(ride is null ? TagStrings.ActionRide : TagStrings.ActionEndRide, RideCommand, IsPrimary: ride is not null));
                var challenge = session.ActiveChallenge(now);
                if (challenge is not null)
                {
                    actions.Add(new(TagStrings.ActionChallengeDone, CompleteChallengeCommand, IsPrimary: true));
                    actions.Add(new(TagStrings.ActionChallengeGiveUp, AbandonChallengeCommand));
                }
                else
                {
                    actions.Add(new(DrawText(session.DrawBlockReason(now)), DrawCommand, IsEnabled: session.DrawBlockReason(now) == DrawBlock.None));
                }

                if (_game.LastLocation is { } here && session.CanReportArrival(here, now))
                {
                    actions.Add(new(TagStrings.ActionArrived, ArrivedCommand, IsPrimary: true));
                }

                actions.Add(new(TagStrings.ActionCaught, CaughtCommand, IsEnabled: session.CanConfirmCatch(now)));
                break;

            case TagPhase.HeadStart or TagPhase.Chase:
                actions.Add(new(TagStrings.ActionRequestCatch, RequestCatchCommand, IsPrimary: true, IsEnabled: session.CanRequestCatch(now)));
                if (session.IsHost)
                {
                    actions.Add(new(TagStrings.ActionForceCatch, CaughtCommand, IsEnabled: session.CanConfirmCatch(now)));
                }

                break;

            case TagPhase.TimeUp when session.IsHost && !session.IsLocalRunner:
                actions.Add(new(TagStrings.ActionSettleLastPing, SettleLastPingCommand, IsPrimary: true, IsEnabled: _game.LastRunnerSighting is not null));
                actions.Add(new(TagStrings.ActionSettleOnMap, SettleOnMapCommand));
                break;
        }

        if (phase != TagPhase.Setup && session.IsHost && phase != TagPhase.GameOver)
        {
            actions.Add(new(TagStrings.ActionAdjustCoins, AdjustCoinsCommand));
        }

        if (phase != TagPhase.Setup)
        {
            actions.Add(new(TagStrings.ActionResults, ResultsCommand, IsPrimary: phase == TagPhase.GameOver));
        }

        return actions;
    }

    private static string DrawText(DrawBlock block) => block switch
    {
        DrawBlock.Cooldown => TagStrings.ActionDrawCooldown,
        DrawBlock.Cursed => TagStrings.ActionDrawCursed,
        _ => TagStrings.ActionDraw,
    };

    // ---- Map -------------------------------------------------------------------------------------------------

    private void UpdateMap(TagSession session)
    {
        var markers = new List<MapMarker>();
        if (session.CurrentTurn is { } turn)
        {
            markers.Add(new MapMarker(turn.StartPoint, MapPalette.Teammate, turn.Index == 0 ? TagStrings.MapStart : TagStrings.MapCatchPoint, 0.5));
            foreach (var player in session.Players.Where(p => p.Destination is not null && session.CanSeeDestination(p)))
            {
                var own = player.Id == session.LocalPlayerId;
                var runners = player.Id == turn.RunnerId;
                markers.Add(new MapMarker(
                    player.Destination!.Location,
                    own ? MapPalette.FinalTarget : runners ? MapPalette.Opponent : MapPalette.Target,
                    string.Format(TagStrings.MapDestinationFormat, player.Name, player.Destination.Name),
                    own || runners ? 0.7 : 0.5));
            }
        }
        else
        {
            foreach (var (playerId, destination) in session.PendingDestinations)
            {
                if (session.Settings.DestinationsVisible || playerId == session.LocalPlayerId)
                {
                    var name = _game.Peers.FirstOrDefault(p => p.DeviceId == playerId)?.Name ?? "?";
                    markers.Add(new MapMarker(
                        destination.Location,
                        playerId == session.LocalPlayerId ? MapPalette.FinalTarget : MapPalette.Target,
                        string.Format(TagStrings.MapDestinationFormat, name, destination.Name),
                        0.55));
                }
            }
        }

        Markers = markers;
    }

    // ---- Announcements ---------------------------------------------------------------------------------------

    /// <summary>Tells the players about a new turn (who runs now) and the end; the runner needs GPS for its pings and rides.</summary>
    private async Task AnnounceAsync(TagSession session)
    {
        try
        {
            if (session.IsLocalRunner)
            {
                await _tracking.EnsureRecordingAsync(session);
            }

            if (!_isVisible || session.CurrentTurn is not { } turn)
            {
                return;
            }

            var current = (session.Id, turn.Index);
            if (_announcedTurn is null)
            {
                _announcedTurn = current; // opening the screen is not news
                _announcedEnd = session.End is not null;
                return;
            }

            if (session.End is not null && !_announcedEnd)
            {
                _announcedEnd = true;
                await _dialogs.AlertAsync(TagStrings.PhaseGameOver, TagTexts.Headline(session.Results(_game.Now)));
                return;
            }

            if (_announcedTurn != current)
            {
                _announcedTurn = current;
                var message = session.IsLocalRunner
                    ? string.Format(TagStrings.NewTurnRunnerFormat, TagTexts.Coins(session.Balance(session.LocalPlayerId)))
                    : string.Format(TagStrings.NewTurnChaserFormat, session.Runner?.Name, TagTexts.Duration(session.Settings.HeadStart));
                await _dialogs.AlertAsync(string.Format(TagStrings.NewTurnTitleFormat, turn.Index + 1), message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Announcing the turn failed");
        }
    }

    private void OnDestinationInReach(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (_isVisible && await _dialogs.ConfirmAsync(TagStrings.ArrivedTitle, TagStrings.ArrivedQuestion, TagStrings.ArrivedYes, TagStrings.ArrivedLater))
                {
                    await _game.ReportArrivalAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Confirming the arrival failed");
            }
        });

    // ---- Commands --------------------------------------------------------------------------------------------

    /// <summary>Opens the map picker and waits for the point (null when cancelled).</summary>
    private async Task<PickedPoint?> PickAsync(string hint, GeoPoint? initial = null)
    {
        var request = new MapPickRequest(hint, Markers, initial);
        await _navigation.GoToAsync(TagUiModule.MapPickRoute, new Dictionary<string, object> { [MapPickViewModel.RequestParameter] = request });
        return await request.Result.Task;
    }

    private async Task<string?> NameDestinationAsync(PickedPoint picked)
    {
        if (picked.Name is { } station)
        {
            return station;
        }

        var name = await _dialogs.PromptAsync(TagStrings.DestinationNameTitle, TagStrings.DestinationNamePrompt, TagStrings.DestinationDefaultName);
        return name is null ? null : string.IsNullOrWhiteSpace(name) ? TagStrings.DestinationDefaultName : name.Trim();
    }

    [RelayCommand]
    private async Task PickOwnDestinationAsync()
    {
        if (Session is not { } session || await PickAsync(TagStrings.PickDestinationHint) is not { } picked
            || await NameDestinationAsync(picked) is not { } name)
        {
            return;
        }

        await _game.SetDestinationAsync(session.LocalPlayerId, picked.Point, name);
    }

    [RelayCommand]
    private async Task SetPlayerDestinationAsync()
    {
        var peers = _game.Peers;
        var choice = await _dialogs.ChooseAsync(TagStrings.ChoosePlayerTitle, [.. peers.Select(p => p.Name)]);
        if (peers.FirstOrDefault(p => p.Name == choice) is not { } peer
            || await PickAsync(string.Format(TagStrings.PickDestinationForFormat, peer.Name)) is not { } picked
            || await NameDestinationAsync(picked) is not { } name)
        {
            return;
        }

        await _game.SetDestinationAsync(peer.DeviceId, picked.Point, name);
    }

    [RelayCommand]
    private async Task StartGameAsync()
    {
        var gps = TagStrings.StartPointGps;
        var map = TagStrings.StartPointMap;
        var choice = await _dialogs.ChooseAsync(TagStrings.StartPointTitle, [gps, map]);
        GeoPoint? start = choice == gps ? await _game.CurrentLocationAsync()
            : choice == map ? (await PickAsync(TagStrings.PickStartHint))?.Point
            : null;
        if (choice is null)
        {
            return;
        }

        if (start is not { } point)
        {
            await _dialogs.AlertAsync(TagStrings.StartPointTitle, TagStrings.NoLocation);
            return;
        }

        var names = string.Join(", ", _game.Peers.Select(p => p.Name));
        if (!await _dialogs.ConfirmAsync(TagStrings.ActionStart, string.Format(TagStrings.StartConfirmFormat, names)))
        {
            return;
        }

        var problem = await _game.StartGameAsync(point);
        if (problem != StartProblem.None)
        {
            await _dialogs.AlertAsync(TagStrings.ActionStart, problem switch
            {
                StartProblem.TooFewPlayers => TagStrings.NoticeWaitingForPlayers,
                StartProblem.DrawFailed => TagStrings.StartDrawFailed,
                _ => TagStrings.StartMissingDestinations,
            });
        }
    }

    [RelayCommand]
    private async Task RideAsync()
    {
        if (Session is not { } session)
        {
            return;
        }

        if (session.OpenRide(session.LocalPlayerId) is not null)
        {
            var distance = await _game.EndRideAsync();
            var ride = session.Rides.LastOrDefault(r => r.PlayerId == session.LocalPlayerId);
            if (ride?.TotalFare is { } total)
            {
                await _dialogs.AlertAsync(
                    TagStrings.RideEndedTitle,
                    distance is { } meters
                        ? string.Format(TagStrings.RideEndedFormat, TagTexts.Distance(meters), total, session.Balance(session.LocalPlayerId))
                        : session.Settings.FareModel == FareModel.PerKm
                            ? string.Format(TagStrings.RideEndedNoTrackFormat, total, session.Balance(session.LocalPlayerId))
                            : string.Format(TagStrings.RideEndedFixedFormat, total, session.Balance(session.LocalPlayerId)));
            }

            return;
        }

        var now = _game.Now;
        var modes = Enum.GetValues<TransportMode>();
        var options = modes.Select(m =>
        {
            var quote = session.QuoteRide(m, now, session.Settings.FareModel == FareModel.Manual ? 0 : null);
            var price = session.Settings.FareModel switch
            {
                FareModel.PerKm => string.Format(TagStrings.PricePerKmFormat, FareCalculator.PerKmRate(session.Settings, m) * quote.Multiplier),
                FareModel.FixedPerRide => TagTexts.Coins(quote.Upfront),
                _ => string.Empty,
            };
            var suffix = quote.Denial == RideDenial.TransportBanned ? " 🚫" : string.Empty;
            return $"{TagTexts.Transport(m)} {price}{suffix}".Trim();
        }).ToList();
        var choice = await _dialogs.ChooseAsync(string.Format(TagStrings.RideChooseFormat, TagTexts.Coins(session.Balance(session.LocalPlayerId))), options);
        if (choice is null)
        {
            return;
        }

        var mode = modes[options.IndexOf(choice)];
        int? manual = null;
        if (session.Settings.FareModel == FareModel.Manual)
        {
            var text = await _dialogs.PromptAsync(TagStrings.RideManualTitle, TagStrings.RideManualPrompt, keyboard: Keyboard.Numeric);
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.CurrentCulture, out var amount))
            {
                return;
            }

            manual = amount;
        }

        var check = session.QuoteRide(mode, _game.Now, manual);
        if (!check.IsAllowed)
        {
            await _dialogs.AlertAsync(TagStrings.RideDeniedTitle, check.Denial == RideDenial.TransportBanned
                ? TagStrings.RideDeniedBanned
                : string.Format(TagStrings.RideDeniedCoinsFormat, check.Upfront, session.Balance(session.LocalPlayerId)));
            return;
        }

        await _game.StartRideAsync(mode, manual);
    }

    [RelayCommand]
    private async Task DrawAsync()
    {
        if (Session is not { } session)
        {
            return;
        }

        await _game.DrawCardAsync();
        if (session.DrawnCards.LastOrDefault(c => c.PlayerId == session.LocalPlayerId) is { } drawn && session.CardOf(drawn) is { } card
            && drawn.DrawnAt >= _game.Now.AddSeconds(-5))
        {
            var details = card.Type == CardType.Curse
                ? string.Format(TagStrings.CurseDrawnFormat, card.LocalizedDescription, card.Reward, TagTexts.CurseDuration(card.CurseDuration),
                    TagTexts.Restriction(card.Restriction) ?? TagStrings.HonourRule)
                : string.Format(TagStrings.ChallengeDrawnFormat, card.LocalizedDescription, card.Reward,
                    card.TimeLimitMinutes is { } minutes ? string.Format(TagStrings.TimeLimitFormat, minutes) : TagStrings.NoTimeLimit);
            await _dialogs.AlertAsync($"{TagTexts.CardType(card.Type)}: {card.LocalizedTitle}", details);
        }
    }

    [RelayCommand]
    private async Task CompleteChallengeAsync()
    {
        if (Session?.ActiveChallenge(_game.Now) is { } drawn && Session.CardOf(drawn) is { } card
            && await _dialogs.ConfirmAsync(card.LocalizedTitle, string.Format(TagStrings.ChallengeDoneConfirmFormat, card.Reward)))
        {
            await _game.ResolveChallengeAsync(drawn, completed: true);
        }
    }

    [RelayCommand]
    private async Task AbandonChallengeAsync()
    {
        if (Session is { } session && session.ActiveChallenge(_game.Now) is { } drawn
            && await _dialogs.ConfirmAsync(TagStrings.ActionChallengeGiveUp, string.Format(TagStrings.ChallengeGiveUpConfirmFormat, TagTexts.Duration(session.Settings.DrawCooldown))))
        {
            await _game.ResolveChallengeAsync(drawn, completed: false);
        }
    }

    [RelayCommand]
    private async Task ArrivedAsync()
    {
        if (await _dialogs.ConfirmAsync(TagStrings.ArrivedTitle, TagStrings.ArrivedQuestion, TagStrings.ArrivedYes, TagStrings.ArrivedLater))
        {
            await _game.ReportArrivalAsync();
        }
    }

    /// <summary>The runner confirms being caught (and by whom); the host can force it.</summary>
    [RelayCommand]
    private async Task CaughtAsync()
    {
        if (Session is not { } session || session.CurrentTurn is not { } turn || !session.CanConfirmCatch(_game.Now))
        {
            return;
        }

        var chasers = session.Players.Where(p => p.Id != turn.RunnerId).ToList();
        var requested = session.PendingCatchRequests.Select(r => r.ChaserId).ToHashSet();
        var options = chasers.Select(p => requested.Contains(p.Id) ? $"{p.Name} ✋" : p.Name).ToList();
        options.Add(TagStrings.CatcherUnknown);
        var title = session.IsLocalRunner ? TagStrings.CaughtWhoTitle : string.Format(TagStrings.ForceCatchTitleFormat, session.Runner?.Name);
        var choice = await _dialogs.ChooseAsync(title, options);
        if (choice is null)
        {
            return;
        }

        var index = options.IndexOf(choice);
        var catcher = index < chasers.Count ? chasers[index].Id : (Guid?)null;
        if (!session.IsLocalRunner && !await _dialogs.ConfirmAsync(TagStrings.ActionForceCatch, TagStrings.ForceCatchWarning))
        {
            return;
        }

        await _game.ConfirmCatchAsync(catcher);
    }

    [RelayCommand]
    private async Task RequestCatchAsync()
    {
        if (Session is { } session && await _dialogs.ConfirmAsync(TagStrings.ActionRequestCatch, string.Format(TagStrings.RequestCatchConfirmFormat, session.Runner?.Name)))
        {
            await _game.RequestCatchAsync();
            await _dialogs.AlertAsync(TagStrings.ActionRequestCatch, TagStrings.RequestCatchSent);
        }
    }

    [RelayCommand]
    private async Task SettleLastPingAsync()
    {
        if (_game.LastRunnerSighting is { } sighting
            && await _dialogs.ConfirmAsync(TagStrings.ActionSettleLastPing, string.Format(TagStrings.SettleLastPingConfirmFormat, sighting.At.ToLocalTime())))
        {
            await _game.SettleTimeUpAsync(sighting.Point, EndPointSource.LastKnown);
        }
    }

    [RelayCommand]
    private async Task SettleOnMapAsync()
    {
        if (await PickAsync(TagStrings.PickRunnerEndHint, _game.LastRunnerSighting?.Point) is { } picked)
        {
            await _game.SettleTimeUpAsync(picked.Point, EndPointSource.Manual);
        }
    }

    [RelayCommand]
    private async Task AdjustCoinsAsync()
    {
        if (Session is not { } session)
        {
            return;
        }

        var players = session.Players.ToList();
        var choice = await _dialogs.ChooseAsync(
            TagStrings.ChoosePlayerTitle,
            [.. players.Select(p => $"{p.Name} · {TagTexts.Coins(session.Balance(p.Id))}")]);
        var index = choice is null ? -1 : players.FindIndex(p => choice.StartsWith(p.Name + " · ", StringComparison.Ordinal));
        if (index < 0)
        {
            return;
        }

        var amountText = await _dialogs.PromptAsync(TagStrings.AdjustTitle, TagStrings.AdjustAmountPrompt, keyboard: Keyboard.Telephone);
        if (!int.TryParse(amountText?.Replace('−', '-'), NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, out var amount) || amount == 0)
        {
            return;
        }

        var reason = await _dialogs.PromptAsync(TagStrings.AdjustTitle, TagStrings.AdjustReasonPrompt);
        if (string.IsNullOrWhiteSpace(reason))
        {
            await _dialogs.AlertAsync(TagStrings.AdjustTitle, TagStrings.AdjustReasonRequired);
            return;
        }

        await _game.AdjustCoinsAsync(players[index].Id, amount, reason.Trim());
    }

    [RelayCommand]
    private Task ResultsAsync() => _navigation.GoToAsync(TagUiModule.ResultsRoute);
}
