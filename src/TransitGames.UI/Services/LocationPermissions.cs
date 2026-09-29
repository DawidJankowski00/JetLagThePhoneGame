namespace TransitGames.UI.Services;

/// <summary>Runtime permissions needed for continuous tracking (shared by the platform trackers).</summary>
public static class LocationPermissions
{
    /// <summary>
    /// Requests "while in use" location (plus notifications on Android 13+, so the recording notification is
    /// visible). Throws <see cref="PermissionException"/> when location is denied.
    /// </summary>
    public static Task EnsureTrackingAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (await RequestAsync<Permissions.LocationWhenInUse>() != PermissionStatus.Granted)
        {
            throw new PermissionException("Location permission was denied.");
        }

        if (DeviceInfo.Platform == DevicePlatform.Android && OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            // Optional: without it the foreground service still runs, only its notification is hidden.
            await RequestAsync<Permissions.PostNotifications>();
        }
    });

    private static async Task<PermissionStatus> RequestAsync<T>()
        where T : Permissions.BasePermission, new()
    {
        var status = await Permissions.CheckStatusAsync<T>();
        return status == PermissionStatus.Granted ? status : await Permissions.RequestAsync<T>();
    }
}
