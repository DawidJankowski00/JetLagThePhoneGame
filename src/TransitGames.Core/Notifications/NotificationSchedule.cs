using System.Text.Json;

namespace TransitGames.Core.Notifications;

/// <summary>
/// The reminders handed to the system, so that planning again only schedules what changed and cancels what is no
/// longer planned (e.g. the round ended early). Persisted by the app, because scheduled reminders outlive it.
/// </summary>
public sealed class NotificationSchedule
{
    private readonly Dictionary<string, ScheduledNotification> _entries;

    public NotificationSchedule(IEnumerable<KeyValuePair<string, ScheduledNotification>>? entries = null) =>
        _entries = entries?.ToDictionary(e => e.Key, e => e.Value) ?? [];

    public IReadOnlyDictionary<string, ScheduledNotification> Entries => _entries;

    /// <summary>System-wide id of a session's notification.</summary>
    public static string Id(Guid sessionId, string key) => $"{sessionId:N}/{key}";

    /// <summary>
    /// Makes <paramref name="plan"/> the reminders of the session. Reminders already due are skipped (and forgotten,
    /// for every session: the system has shown them).
    /// </summary>
    public NotificationChanges Replace(Guid sessionId, IEnumerable<GameNotification> plan, DateTimeOffset now)
    {
        foreach (var due in _entries.Where(e => e.Value.At <= now).Select(e => e.Key).ToList())
        {
            _entries.Remove(due);
        }

        var wanted = new Dictionary<string, ScheduledNotification>();
        foreach (var n in plan)
        {
            if (n.At is { } at && at > now)
            {
                wanted[Id(sessionId, n.Key)] = new ScheduledNotification(n.Title, n.Body, at);
            }
        }

        var prefix = Id(sessionId, string.Empty);
        var cancel = _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && !wanted.ContainsKey(k)).ToList();
        foreach (var id in cancel)
        {
            _entries.Remove(id);
        }

        var schedule = wanted.Where(w => !_entries.TryGetValue(w.Key, out var known) || known != w.Value).ToList();
        foreach (var (id, entry) in schedule)
        {
            _entries[id] = entry;
        }

        return new NotificationChanges(schedule, cancel);
    }

    /// <summary>Forgets all reminders of the session; returns the ids to cancel.</summary>
    public IReadOnlyList<string> Clear(Guid sessionId) => Replace(sessionId, [], DateTimeOffset.MinValue).ToCancel;

    /// <summary>Forgets every reminder (notifications switched off); returns the ids to cancel.</summary>
    public IReadOnlyList<string> ClearAll()
    {
        var ids = _entries.Keys.ToList();
        _entries.Clear();
        return ids;
    }

    public string Serialize() => JsonSerializer.Serialize(_entries);

    /// <summary>Reads <see cref="Serialize"/> output; an empty schedule for missing or damaged data.</summary>
    public static NotificationSchedule Parse(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return new();
        }

        try
        {
            return new(JsonSerializer.Deserialize<Dictionary<string, ScheduledNotification>>(json));
        }
        catch (JsonException)
        {
            return new();
        }
    }
}

public sealed record ScheduledNotification(string Title, string Body, DateTimeOffset At);

/// <param name="ToSchedule">New or changed reminders (scheduling with a known id replaces it).</param>
/// <param name="ToCancel">Ids of reminders that are no longer planned.</param>
public sealed record NotificationChanges(
    IReadOnlyList<KeyValuePair<string, ScheduledNotification>> ToSchedule,
    IReadOnlyList<string> ToCancel);
