namespace TransitGames.UI.Notifications;

/// <summary>
/// Local system notifications of the platform. Ids are strings; the same id replaces a shown notification or a
/// scheduled one. Scheduled notifications fire even when the app is not running.
/// </summary>
public interface INotificationScheduler
{
    /// <summary>Asks for the permission to notify when the system requires it; false when notifications are blocked.</summary>
    Task<bool> RequestPermissionAsync();

    void Show(string id, string title, string body);

    void Schedule(string id, string title, string body, DateTimeOffset at);

    void Cancel(string id);
}

/// <summary>For platforms without notifications.</summary>
public sealed class NullNotificationScheduler : INotificationScheduler
{
    public Task<bool> RequestPermissionAsync() => Task.FromResult(false);

    public void Show(string id, string title, string body)
    {
    }

    public void Schedule(string id, string title, string body, DateTimeOffset at)
    {
    }

    public void Cancel(string id)
    {
    }
}
