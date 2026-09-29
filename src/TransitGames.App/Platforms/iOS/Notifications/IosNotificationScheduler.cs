using Foundation;
using TransitGames.UI.Notifications;
using UserNotifications;

namespace TransitGames.App.Notifications;

/// <summary>
/// Game notifications on iOS: local notification requests (immediate or with a time trigger) of
/// <see cref="UNUserNotificationCenter"/>; the id of a request replaces an earlier one with the same id.
/// </summary>
public sealed class IosNotificationScheduler : INotificationScheduler
{
    public async Task<bool> RequestPermissionAsync()
    {
        var (granted, _) = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(
            UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound | UNAuthorizationOptions.Badge);
        return granted;
    }

    public void Show(string id, string title, string body) => Add(id, title, body, null);

    public void Schedule(string id, string title, string body, DateTimeOffset at)
    {
        var seconds = Math.Max(1, (at - DateTimeOffset.UtcNow).TotalSeconds);
        Add(id, title, body, UNTimeIntervalNotificationTrigger.CreateTrigger(seconds, false));
    }

    public void Cancel(string id)
    {
        UNUserNotificationCenter.Current.RemovePendingNotificationRequests([id]);
        UNUserNotificationCenter.Current.RemoveDeliveredNotifications([id]);
    }

    private static void Add(string id, string title, string body, UNNotificationTrigger? trigger)
    {
        var content = new UNMutableNotificationContent
        {
            Title = title,
            Body = body,
            Sound = UNNotificationSound.Default,
        };
        var request = UNNotificationRequest.FromIdentifier(id, content, trigger);
        UNUserNotificationCenter.Current.AddNotificationRequest(request, _ => { });
    }
}

/// <summary>Shows notifications also while the app is open (iOS hides them by default).</summary>
public sealed class ForegroundNotificationDelegate : UNUserNotificationCenterDelegate
{
    public override void WillPresentNotification(
        UNUserNotificationCenter center,
        UNNotification notification,
        Action<UNNotificationPresentationOptions> completionHandler) =>
        completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List | UNNotificationPresentationOptions.Sound);
}
