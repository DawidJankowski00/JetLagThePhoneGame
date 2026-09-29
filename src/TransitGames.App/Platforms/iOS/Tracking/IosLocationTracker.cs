using CoreLocation;
using Microsoft.Extensions.Logging;
using TransitGames.Core.Geography;
using TransitGames.Core.Location;
using TransitGames.UI.Services;

namespace TransitGames.App.Tracking;

/// <summary>
/// Continuous location on iOS: <see cref="CLLocationManager"/> with background updates enabled
/// (<c>UIBackgroundModes: location</c> in Info.plist), so recording goes on with the screen locked.
/// iOS has no update interval; <c>TrackPointFilter</c> thins the fixes.
/// </summary>
public sealed class IosLocationTracker : ILocationTracker
{
    private readonly IGeolocation _geolocation;
    private readonly ILogger<IosLocationTracker> _logger;
    private CLLocationManager? _manager;

    public IosLocationTracker(IGeolocation geolocation, ILogger<IosLocationTracker> logger)
    {
        _geolocation = geolocation;
        _logger = logger;
    }

    public bool IsTracking => _manager is not null;

    public LocationSample? LastLocation { get; private set; }

    public event EventHandler<LocationSample>? LocationChanged;

    public async Task<LocationSample?> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        var location = await _geolocation.GetLocationAsync(
            new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(10)), cancellationToken);
        return location is null
            ? LastLocation
            : new LocationSample(new GeoPoint(location.Latitude, location.Longitude), location.Timestamp, location.Accuracy, location.Altitude);
    }

    public async Task StartAsync(TrackingOptions options, CancellationToken cancellationToken = default)
    {
        await LocationPermissions.EnsureTrackingAsync();
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            StopUpdates();
            var manager = new CLLocationManager
            {
                DesiredAccuracy = options.BatterySaver ? CLLocation.AccuracyHundredMeters : CLLocation.AccuracyBest,
                DistanceFilter = CLLocationDistance.FilterNone, // distance filtering happens in TrackPointFilter
                PausesLocationUpdatesAutomatically = false,
                AllowsBackgroundLocationUpdates = true,
                ShowsBackgroundLocationIndicator = true,
                ActivityType = CLActivityType.OtherNavigation,
            };
            manager.LocationsUpdated += OnLocationsUpdated;
            manager.StartUpdatingLocation();
            _manager = manager;
            _logger.LogInformation("Location updates started (battery saver: {BatterySaver})", options.BatterySaver);
        });
    }

    public Task StopAsync() => MainThread.InvokeOnMainThreadAsync(StopUpdates);

    private void StopUpdates()
    {
        if (_manager is { } manager)
        {
            manager.StopUpdatingLocation();
            manager.LocationsUpdated -= OnLocationsUpdated;
            manager.Dispose();
            _manager = null;
        }
    }

    private void OnLocationsUpdated(object? sender, CLLocationsUpdatedEventArgs e)
    {
        foreach (var location in e.Locations)
        {
            if (location.HorizontalAccuracy < 0)
            {
                continue; // invalid fix
            }

            var sample = new LocationSample(
                new GeoPoint(location.Coordinate.Latitude, location.Coordinate.Longitude),
                DateTimeOffset.FromUnixTimeMilliseconds((long)(location.Timestamp.SecondsSince1970 * 1000)),
                location.HorizontalAccuracy,
                location.VerticalAccuracy >= 0 ? location.Altitude : null,
                location.Speed >= 0 ? location.Speed : null);
            LastLocation = sample;
            LocationChanged?.Invoke(this, sample);
        }
    }
}
