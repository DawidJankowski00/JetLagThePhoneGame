using Microsoft.Extensions.Logging;
using TransitGames.Core.Geography;

namespace TransitGames.UI.Services;

/// <summary>One-off "where am I" (map centring, custom stations). Continuous tracking is ILocationTracker.</summary>
public interface IDeviceLocationService
{
    /// <summary>Current position, or null when permission is denied or location is unavailable.</summary>
    Task<GeoPoint?> TryGetCurrentAsync(CancellationToken cancellationToken = default);
}

public sealed class DeviceLocationService : IDeviceLocationService
{
    private readonly IGeolocation _geolocation;
    private readonly ILogger<DeviceLocationService> _logger;

    public DeviceLocationService(IGeolocation geolocation, ILogger<DeviceLocationService> logger)
    {
        _geolocation = geolocation;
        _logger = logger;
    }

    public async Task<GeoPoint?> TryGetCurrentAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var current = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                return current == PermissionStatus.Granted
                    ? current
                    : await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            });
            if (status != PermissionStatus.Granted)
            {
                return null;
            }

            var location = await _geolocation.GetLocationAsync(
                    new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)), cancellationToken)
                ?? await _geolocation.GetLastKnownLocationAsync();
            return location is null ? null : new GeoPoint(location.Latitude, location.Longitude);
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or FeatureNotEnabledException
                                       or PermissionException or TaskCanceledException)
        {
            _logger.LogInformation(ex, "Location unavailable");
            return null;
        }
    }
}
