using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransitGames.Core.Geography;
using TransitGames.Core.Stations;
using TransitGames.Games.Tag.Maui.Services;
using TransitGames.Games.Tag.Resources;
using TransitGames.Geo.Areas;
using TransitGames.UI.Maps;
using TransitGames.UI.Services;

namespace TransitGames.Games.Tag.Maui.ViewModels;

/// <summary>A point picked on the map; <see cref="Name"/> is the nearest station's when the tap was close to one.</summary>
public sealed record PickedPoint(GeoPoint Point, string? Name);

/// <summary>What to pick: shown as the hint; <see cref="Context"/> are markers for orientation (other destinations...).</summary>
public sealed record MapPickRequest(string Hint, IReadOnlyList<MapMarker> Context, GeoPoint? Initial = null)
{
    public TaskCompletionSource<PickedPoint?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Picks one point on the map (a destination, the start point, the runner's position at the end): a tap places it,
/// "my position" uses GPS, a tap near a station snaps to it.
/// </summary>
public sealed partial class MapPickViewModel : ObservableObject, IQueryAttributable
{
    public const string RequestParameter = "pickRequest";

    /// <summary>A tap closer than this to a station picks the station.</summary>
    private const double SnapMeters = 150;

    private readonly TagController _game;
    private readonly INavigationService _navigation;
    private MapPickRequest? _request;
    private bool _answered;

    public MapPickViewModel(TagController game, INavigationService navigation)
    {
        _game = game;
        _navigation = navigation;
    }

    [ObservableProperty]
    public partial GameArea? Area { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<Station> Stations { get; set; } = [];

    [ObservableProperty]
    public partial string Hint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<MapMarker> Markers { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickedText))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial PickedPoint? Picked { get; set; }

    public string PickedText => Picked is { } picked ? picked.Name ?? picked.Point.ToString() : TagStrings.PickNothing;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(RequestParameter, out var value) && value is MapPickRequest request)
        {
            _request = request;
            _answered = false;
            Hint = request.Hint;
            Area = _game.Area;
            Stations = _game.Session?.Settings.Stations ?? [];
            Picked = request.Initial is { } initial ? new PickedPoint(initial, null) : null;
            Refresh();
        }
    }

    /// <summary>Leaving without confirming counts as cancel.</summary>
    public void OnDisappearing() => Answer(null);

    [RelayCommand]
    private void MapTapped(GeoPoint point) => Pick(point);

    [RelayCommand]
    private async Task UseMyPositionAsync()
    {
        if (await _game.CurrentLocationAsync() is { } here)
        {
            Picked = new PickedPoint(here, null);
            Refresh();
        }
    }

    private bool CanConfirm() => Picked is not null;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        Answer(Picked);
        await _navigation.GoBackAsync();
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        Answer(null);
        await _navigation.GoBackAsync();
    }

    private void Pick(GeoPoint point)
    {
        var station = Stations
            .Where(s => s.IsEnabled)
            .Select(s => (Station: s, Distance: s.Location.DistanceTo(point)))
            .Where(x => x.Distance <= SnapMeters)
            .OrderBy(x => x.Distance)
            .Select(x => x.Station)
            .FirstOrDefault();
        Picked = station is null ? new PickedPoint(point, null) : new PickedPoint(station.Location, station.Name);
        Refresh();
    }

    private void Answer(PickedPoint? result)
    {
        if (!_answered && _request is { } request)
        {
            _answered = true;
            request.Result.TrySetResult(result);
        }
    }

    private void Refresh()
    {
        var markers = new List<MapMarker>(_request?.Context ?? []);
        if (Picked is { } picked)
        {
            markers.Add(new MapMarker(picked.Point, MapPalette.FinalTarget, picked.Name ?? TagStrings.PickHere, 0.7));
        }

        Markers = markers;
    }
}
