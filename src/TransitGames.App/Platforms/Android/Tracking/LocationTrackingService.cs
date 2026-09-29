using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using TransitGames.UI.Resources;

namespace TransitGames.App.Tracking;

/// <summary>
/// Foreground service (type location) with an ongoing notification. It keeps the process alive and allowed to
/// receive location while the screen is off; the fixes themselves go to <see cref="AndroidLocationTracker"/>.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeLocation)]
public sealed class LocationTrackingService : Service
{
    private const int NotificationId = 4101;
    private const string ChannelId = "tracking";

    public static void Start(Context context) =>
        context.StartForegroundService(new Intent(context, typeof(LocationTrackingService)));

    public static void Stop(Context context) =>
        context.StopService(new Intent(context, typeof(LocationTrackingService)));

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var notification = BuildNotification();
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeLocation);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }

        // Not sticky: restarting a location service from the background is not allowed on recent Android versions.
        // After the process is killed the game (and recording) resumes when the app is opened again.
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    private Notification BuildNotification()
    {
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(
            new NotificationChannel(ChannelId, UiStrings.TrackingNotificationChannel, NotificationImportance.Low));

        var launch = PackageManager!.GetLaunchIntentForPackage(PackageName!)!;
        launch.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var contentIntent = PendingIntent.GetActivity(this, 0, launch, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var builder = new NotificationCompat.Builder(this, ChannelId);
        builder.SetContentTitle(UiStrings.TrackingNotificationTitle);
        builder.SetContentText(UiStrings.TrackingNotificationText);
        builder.SetSmallIcon(Resource.Drawable.ic_stat_tracking);
        builder.SetOngoing(true);
        builder.SetOnlyAlertOnce(true);
        builder.SetContentIntent(contentIntent);
        builder.SetCategory(NotificationCompat.CategoryNavigation);
        builder.SetForegroundServiceBehavior(NotificationCompat.ForegroundServiceImmediate);
        return builder.Build()!;
    }
}
