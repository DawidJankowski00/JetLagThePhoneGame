using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TransitGames.Tracking;
using TransitGames.Tracking.Export;
using TransitGames.Tracking.Storage;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;

namespace TransitGames.App.ViewModels;

public sealed record TrackItem(TrackInfo Track, bool IsRecording)
{
    public string Title => Track.Name;

    public string Subtitle =>
        $"{Track.StartedAt.ToLocalTime():g} · "
        + string.Format(UiStrings.TrackStatsFormat, DisplayFormat.Duration(Track.Duration), DisplayFormat.Distance(Track.DistanceMeters), Track.PointCount);

    public string Badge => IsRecording ? UiStrings.RecordingIndicator : Track.IsOpen ? UiStrings.TrackOpenBadge : string.Empty;
}

/// <summary>"GPS tracks" tab: recording status, manual recording, list of tracks with preview/share/delete.</summary>
public sealed partial class TracksViewModel : ObservableObject
{
    private readonly ITrackStore _store;
    private readonly TrackRecorder _recorder;
    private readonly TrackingCoordinator _tracking;
    private readonly TrackExporter _exporter;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IShare _share;
    private readonly ILogger<TracksViewModel> _logger;

    public TracksViewModel(
        ITrackStore store,
        TrackRecorder recorder,
        TrackingCoordinator tracking,
        TrackExporter exporter,
        IDialogService dialogs,
        INavigationService navigation,
        IShare share,
        ILogger<TracksViewModel> logger)
    {
        _store = store;
        _recorder = recorder;
        _tracking = tracking;
        _exporter = exporter;
        _dialogs = dialogs;
        _navigation = navigation;
        _share = share;
        _logger = logger;
    }

    public ObservableCollection<TrackItem> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    [NotifyPropertyChangedFor(nameof(IsNotRecording))]
    public partial bool IsRecording { get; set; }

    public bool IsNotRecording => !IsRecording;

    [ObservableProperty]
    public partial string RecordingText { get; set; } = string.Empty;

    public async Task OnAppearingAsync()
    {
        _recorder.StateChanged -= OnRecorderStateChanged;
        _recorder.StateChanged += OnRecorderStateChanged;
        await LoadAsync();
    }

    public void OnDisappearing() => _recorder.StateChanged -= OnRecorderStateChanged;

    private async Task LoadAsync()
    {
        var tracks = await _store.ListAsync();
        var active = _recorder.ActiveTrack;
        Items.Clear();
        foreach (var track in tracks)
        {
            Items.Add(new TrackItem(active?.Id == track.Id ? active : track, active?.Id == track.Id));
        }

        IsEmpty = Items.Count == 0;
        UpdateRecordingState();
    }

    private void UpdateRecordingState()
    {
        var active = _recorder.ActiveTrack;
        IsRecording = active is not null;
        RecordingText = active is null
            ? string.Empty
            : string.Format(UiStrings.TracksRecordingFormat, active.Name) + Environment.NewLine
              + string.Format(UiStrings.TrackStatsFormat, DisplayFormat.Duration(DateTimeOffset.UtcNow - active.StartedAt),
                  DisplayFormat.Distance(active.DistanceMeters), active.PointCount);
    }

    private void OnRecorderStateChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // New track or stop: reload the list; new point: only the header and the recorded row change.
            var active = _recorder.ActiveTrack;
            var index = active is null ? -1 : Items.ToList().FindIndex(i => i.Track.Id == active.Id);
            if (index >= 0 && Items.Count(i => i.IsRecording) == 1 && Items[index].IsRecording)
            {
                Items[index] = new TrackItem(active!, true);
                UpdateRecordingState();
            }
            else
            {
                await LoadAsync();
            }
        });

    private bool CanStartRecording() => !IsRecording;

    [RelayCommand(CanExecute = nameof(CanStartRecording))]
    private async Task StartRecordingAsync()
    {
        await _tracking.StartManualAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task StopRecordingAsync()
    {
        await _tracking.StopAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task OpenAsync(TrackItem item)
    {
        string[] actions = [UiStrings.TrackPreview, UiStrings.TrackShareGpx, UiStrings.TrackShareGeoJson, UiStrings.TrackDelete];
        var action = await _dialogs.ChooseAsync(item.Title, actions);
        if (action == UiStrings.TrackPreview)
        {
            await PreviewAsync(item);
        }
        else if (action == UiStrings.TrackShareGpx)
        {
            await ShareAsync(item, TrackFileFormat.Gpx);
        }
        else if (action == UiStrings.TrackShareGeoJson)
        {
            await ShareAsync(item, TrackFileFormat.GeoJson);
        }
        else if (action == UiStrings.TrackDelete)
        {
            await DeleteAsync(item);
        }
    }

    private async Task PreviewAsync(TrackItem item)
    {
        if (item.Track.PointCount == 0)
        {
            await _dialogs.AlertAsync(item.Title, UiStrings.TrackNoPoints);
            return;
        }

        await _navigation.GoToAsync(Routes.TrackPreview, new Dictionary<string, object> { [TrackPreviewViewModel.TrackIdKey] = item.Track.Id });
    }

    private async Task ShareAsync(TrackItem item, TrackFileFormat format)
    {
        try
        {
            var path = await _exporter.ExportToFileAsync(item.Track.Id, format, Path.Combine(FileSystem.CacheDirectory, "exports"));
            await _share.RequestAsync(new ShareFileRequest
            {
                Title = item.Title,
                File = new ShareFile(path, TrackExporter.ContentType(format)),
            });
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            _logger.LogError(ex, "Export failed");
            await _dialogs.AlertAsync(item.Title, ex.Message);
        }
    }

    private async Task DeleteAsync(TrackItem item)
    {
        if (item.IsRecording)
        {
            await _dialogs.AlertAsync(item.Title, UiStrings.TrackDeleteRecording);
            return;
        }

        if (await _dialogs.ConfirmAsync(item.Title, string.Format(UiStrings.TrackDeleteConfirm, item.Title)))
        {
            await _store.DeleteAsync(item.Track.Id);
            await LoadAsync();
        }
    }
}
