using Android.Content;
using Android.Locations;
using Android.OS;
using Microsoft.Extensions.Logging;
using TransitGames.Core.Geography;
using TransitGames.Core.Location;
using TransitGames.UI.Services;
using AndroidLocation = Android.Locations.Location;

namespace TransitGames.App.Tracking;

/// <summary>
/// Continuous location on Android: <see cref="LocationManager"/> updates (fused provider on Android 12+, GPS
/// before) while <see cref="LocationTrackingService"/> runs in the foreground.
/// </summary>
public sealed class AndroidLocationTracker : ILocationTracker
{
    private readonly IGeolocation _geolocation;
    private readonly ILogger<AndroidLocationTracker> _logger;
    private Listener? _listener;

    public AndroidLocationTracker(IGeolocation geolocation, ILogger<AndroidLocationTracker> logger)
    {
        _geolocation = geolocation;
        _logger = logger;
    }

    public bool IsTracking => _listener is not null;

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
            var context = Platform.AppContext;
            var manager = (LocationManager)context.GetSystemService(Context.LocationService)!;
            StopUpdates(manager);

            LocationTrackingService.Start(context);
            var listener = new Listener(this);
            var provider = ChooseProvider(manager);
            var intervalMs = (long)options.Interval.TotalMilliseconds;
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                var quality = options.BatterySaver ? LocationRequestQuality.BalancedPowerAccuracy : LocationRequestQuality.HighAccuracy;
                var request = new LocationRequest.Builder(intervalMs)
                    .SetQuality((int)quality) // the binding takes the raw int
                    .SetMinUpdateDistanceMeters(0) // distance filtering happens in TrackPointFilter
                    .Build();
                manager.RequestLocationUpdates(provider, request, context.MainExecutor!, listener);
            }
            else
            {
                manager.RequestLocationUpdates(provider, intervalMs, 0f, listener, Looper.MainLooper);
            }

            _listener = listener;
            _logger.LogInformation("Location updates from {Provider} every {Interval} ms", provider, intervalMs);
        });
    }

    public Task StopAsync() => MainThread.InvokeOnMainThreadAsync(() =>
    {
        var context = Platform.AppContext;
        StopUpdates((LocationManager)context.GetSystemService(Context.LocationService)!);
        LocationTrackingService.Stop(context);
    });

    private void StopUpdates(LocationManager manager)
    {
        if (_listener is { } listener)
        {
            manager.RemoveUpdates(listener);
            _listener = null;
        }
    }

    private static string ChooseProvider(LocationManager manager)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31) && manager.AllProviders.Contains(LocationManager.FusedProvider))
        {
            return LocationManager.FusedProvider;
        }

        return manager.IsProviderEnabled(LocationManager.GpsProvider) || !manager.IsProviderEnabled(LocationManager.NetworkProvider)
            ? LocationManager.GpsProvider
            : LocationManager.NetworkProvider;
    }

    private void OnLocation(AndroidLocation location)
    {
        var sample = new LocationSample(
            new GeoPoint(location.Latitude, location.Longitude),
            DateTimeOffset.FromUnixTimeMilliseconds(location.Time),
            location.HasAccuracy ? location.Accuracy : null,
            location.HasAltitude ? location.Altitude : null,
            location.HasSpeed ? location.Speed : null);
        LastLocation = sample;
        LocationChanged?.Invoke(this, sample);
    }

    private sealed class Listener(AndroidLocationTracker owner) : Java.Lang.Object, ILocationListener
    {
        public void OnLocationChanged(AndroidLocation location) => owner.OnLocation(location);

        public void OnProviderDisabled(string provider) => owner._logger.LogInformation("Location provider {Provider} disabled", provider);

        public void OnProviderEnabled(string provider) => owner._logger.LogInformation("Location provider {Provider} enabled", provider);

        // Deprecated in API 29 and never called there, but still part of the bound interface.
        public void OnStatusChanged(string? provider, Availability status, Bundle? extras)
        {
        }
    }
}
