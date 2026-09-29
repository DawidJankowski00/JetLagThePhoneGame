using Microsoft.Extensions.Logging;
using TransitGames.Core.Games;
using TransitGames.Core.Sessions;
using TransitGames.Tracking;
using TransitGames.Tracking.Storage;
using TransitGames.UI.Resources;
using TransitGames.UI.Settings;

namespace TransitGames.UI.Services;

/// <summary>
/// Connects games with track recording: once a game screen has been shown, the game's track is recorded (continued
/// after an app restart), finishing the game closes the track. Leaving the game screen does not stop recording —
/// the game goes on in the real world. Also starts/stops manual recordings from the tracks tab.
/// </summary>
public sealed class TrackingCoordinator
{
    private readonly GameSessionManager _sessions;
    private readonly GameRegistry _registry;
    private readonly TrackRecorder _recorder;
    private readonly ITrackStore _store;
    private readonly TrackingPreferences _preferences;
    private readonly IDialogService _dialogs;
    private readonly IDeviceInfo _device;
    private readonly ILogger<TrackingCoordinator> _logger;
    private IGameSession? _currentSession;

    public TrackingCoordinator(
        GameSessionManager sessions,
        GameRegistry registry,
        TrackRecorder recorder,
        ITrackStore store,
        TrackingPreferences preferences,
        IDialogService dialogs,
        IDeviceInfo device,
        ILogger<TrackingCoordinator> logger)
    {
        _sessions = sessions;
        _registry = registry;
        _recorder = recorder;
        _store = store;
        _preferences = preferences;
        _dialogs = dialogs;
        _device = device;
        _logger = logger;
        _sessions.CurrentChanged += OnCurrentSessionChanged;
    }

    public Task<bool> StartManualAsync() =>
        TryStartAsync(new TrackStartRequest(UiStrings.TracksManualName, _device.Name));

    /// <summary>Stops the current recording; a game's track stays open and continues when the game is resumed.</summary>
    public Task StopAsync() => _recorder.ActiveTrack?.SessionId is null ? _recorder.StopAsync() : _recorder.PauseAsync();

    private async void OnCurrentSessionChanged(object? sender, EventArgs e)
    {
        try
        {
            var current = _sessions.Current;
            if (current is null)
            {
                // Only a finished game ends its track; a left game keeps recording.
                if (_currentSession is { Status: SessionStatus.Finished } finished)
                {
                    _currentSession = null;
                    await CloseTrackAsync(finished.Id);
                }

                return;
            }

            _currentSession = current;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Track recording coordination failed");
        }
    }

    /// <summary>
    /// Called when a game screen is shown (new game, resumed game, app restarted into a game). Recording starts
    /// here rather than when a session becomes current: the start screen also loads an unfinished session just to
    /// offer "resume", and that must not start the GPS.
    /// </summary>
    public async void OnGameScreenShown(IGameSession session)
    {
        try
        {
            if (session.Status != SessionStatus.Finished && _preferences.AutoRecord && _recorder.ActiveTrack?.SessionId != session.Id)
            {
                await TryStartAsync(RequestFor(session));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Track recording could not start");
        }
    }

    private async Task CloseTrackAsync(Guid sessionId)
    {
        if (_recorder.ActiveTrack?.SessionId == sessionId)
        {
            await _recorder.StopAsync();
        }
        else if (await _store.FindOpenAsync(sessionId) is { } paused)
        {
            await _store.CloseAsync(paused.Id, paused.LastPointAt ?? paused.StartedAt);
        }
    }

    private TrackStartRequest RequestFor(IGameSession session)
    {
        var module = _registry.TryGet(session.GameId, out var m) ? m : null;
        var role = module?.Roles.FirstOrDefault(r => r.Id == session.LocalRoleId)?.DisplayName ?? session.LocalRoleId;
        var name = module is null ? role : $"{module.DisplayName} · {role}";
        return new TrackStartRequest(name, _device.Name, session.Id, session.GameId, session.LocalRoleId);
    }

    private async Task<bool> TryStartAsync(TrackStartRequest request)
    {
        try
        {
            await _recorder.StartAsync(request, _preferences.ToOptions());
            return true;
        }
        catch (PermissionException ex)
        {
            _logger.LogInformation(ex, "No location permission for tracking");
            await MainThread.InvokeOnMainThreadAsync(
                () => _dialogs.AlertAsync(UiStrings.TrackingFailedTitle, UiStrings.TrackingPermissionDenied));
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or FeatureNotEnabledException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Tracking could not start");
            await MainThread.InvokeOnMainThreadAsync(
                () => _dialogs.AlertAsync(UiStrings.TrackingFailedTitle, string.Format(UiStrings.TrackingUnavailable, ex.Message)));
        }

        return false;
    }
}
