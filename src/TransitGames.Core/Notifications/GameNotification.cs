namespace TransitGames.Core.Notifications;

/// <summary>
/// A system notification about a game: a reminder planned for a moment ahead (<see cref="At"/>) or news shown at
/// once (<see cref="At"/> null) while the app is in the background.
/// </summary>
/// <param name="Key">
/// Identifies the notification within its session: planning again with the same key replaces the reminder, a new
/// notification with the key of a shown one replaces it on screen.
/// </param>
public sealed record GameNotification(string Key, string Title, string Body, DateTimeOffset? At = null);
