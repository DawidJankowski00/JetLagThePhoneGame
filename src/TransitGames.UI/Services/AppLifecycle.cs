namespace TransitGames.UI.Services;

/// <summary>Whether the app is on screen; news is notified only while it is not (on screen the game shows it itself).</summary>
public sealed class AppLifecycle
{
    public bool IsInForeground { get; private set; } = true;

    /// <summary>Follows the app window (called once when it is created).</summary>
    public void Attach(Window window)
    {
        window.Activated += (_, _) => IsInForeground = true;
        window.Resumed += (_, _) => IsInForeground = true;
        window.Stopped += (_, _) => IsInForeground = false;
    }
}
