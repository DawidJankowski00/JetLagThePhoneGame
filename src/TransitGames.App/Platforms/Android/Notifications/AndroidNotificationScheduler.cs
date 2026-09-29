using Android.App;
using Android.Content;
using AndroidX.Core.App;
using TransitGames.UI.Notifications;
using TransitGames.UI.Resources;

namespace TransitGames.App.Notifications;

/// <summary>
/// Game notifications on Android: shown through <see cref="NotificationManagerCompat"/>, scheduled with
/// <see cref="AlarmManager"/> (exact when the system allows it, otherwise inexact) and posted by
/// <see cref="GameNotificationReceiver"/> — also when the app is not running. Scheduled alarms do not survive a
/// reboot of the phone (see docs/DECISIONS.md).
/// </summary>
public sealed class AndroidNotificationScheduler : INotificationScheduler
{
    internal const string ExtraId = "id";
    internal const string ExtraTitle = "title";
    internal const string ExtraBody = "body";
    private const string ChannelId = "game";

    public async Task<bool> RequestPermissionAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return NotificationManagerCompat.From(Platform.AppContext)!.AreNotificationsEnabled();
        }

        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        if (status != PermissionStatus.Granted)
        {
            status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.PostNotifications>);
        }

        return status == PermissionStatus.Granted;
    }

    public void Show(string id, string title, string body) => Post(Platform.AppContext, id, title, body);

    public void Schedule(string id, string title, string body, DateTimeOffset at)
    {
        var context = Platform.AppContext;
        var alarms = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
        var intent = AlarmIntent(context, id, title, body);
        var when = at.ToUnixTimeMilliseconds();
        if (OperatingSystem.IsAndroidVersionAtLeast(31) && !alarms.CanScheduleExactAlarms())
        {
            alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, when, intent);
        }
        else
        {
            alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, when, intent);
        }
    }

    public void Cancel(string id)
    {
        var context = Platform.AppContext;
        var alarms = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
        alarms.Cancel(AlarmIntent(context, id, string.Empty, string.Empty));
        NotificationManagerCompat.From(context)!.Cancel(NotificationId(id));
    }

    internal static void Post(Context context, string id, string title, string body)
    {
        var manager = NotificationManagerCompat.From(context)!;
        if (!manager.AreNotificationsEnabled())
        {
            return;
        }

        var system = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        system.CreateNotificationChannel(
            new NotificationChannel(ChannelId, UiStrings.NotificationChannelGame, NotificationImportance.High));

        var launch = context.PackageManager!.GetLaunchIntentForPackage(context.PackageName!)!;
        launch.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var open = PendingIntent.GetActivity(context, 0, launch, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var builder = new NotificationCompat.Builder(context, ChannelId);
        builder.SetSmallIcon(Resource.Drawable.ic_stat_game);
        builder.SetContentTitle(title);
        builder.SetContentText(body);
        builder.SetStyle(new NotificationCompat.BigTextStyle().BigText(body));
        builder.SetPriority(NotificationCompat.PriorityHigh);
        builder.SetCategory(NotificationCompat.CategoryReminder);
        builder.SetAutoCancel(true);
        builder.SetContentIntent(open);
        manager.Notify(NotificationId(id), builder.Build()!);
    }

    /// <summary>
    /// The alarm's intent. Android matches pending intents by request code and data (not extras), so both come from
    /// the id — cancelling with the same id finds the scheduled alarm.
    /// </summary>
    private static PendingIntent AlarmIntent(Context context, string id, string title, string body)
    {
        var intent = new Intent(context, typeof(GameNotificationReceiver));
        intent.SetData(Android.Net.Uri.FromParts("transitgames", id, null));
        intent.PutExtra(ExtraId, id);
        intent.PutExtra(ExtraTitle, title);
        intent.PutExtra(ExtraBody, body);
        return PendingIntent.GetBroadcast(context, NotificationId(id), intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }

    /// <summary>Stable across app runs (unlike <see cref="string.GetHashCode()"/>): FNV-1a of the id.</summary>
    private static int NotificationId(string id)
    {
        var hash = 2166136261u;
        foreach (var c in id)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return (int)(hash & 0x7FFFFFFF);
    }
}

/// <summary>Posts a scheduled game notification when its alarm goes off.</summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class GameNotificationReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.GetStringExtra(AndroidNotificationScheduler.ExtraId) is not { } id)
        {
            return;
        }

        AndroidNotificationScheduler.Post(
            context,
            id,
            intent.GetStringExtra(AndroidNotificationScheduler.ExtraTitle) ?? string.Empty,
            intent.GetStringExtra(AndroidNotificationScheduler.ExtraBody) ?? string.Empty);
    }
}
