using TransitGames.Core.Geography;

namespace TransitGames.Core.Events;

/// <summary>An entry on the game timeline. Also exported as GPX/GeoJSON waypoints when it has a location.</summary>
public sealed record GameEvent
{
    public required Guid Id { get; init; }
    public required Guid SessionId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Machine-readable type, see <see cref="GameEventTypes"/> (games may define their own).</summary>
    public required string Type { get; init; }

    /// <summary>Human-readable, already localized title.</summary>
    public required string Title { get; init; }

    public string? Details { get; init; }
    public string? ActorRoleId { get; init; }
    public GeoPoint? Location { get; init; }
    public IReadOnlyDictionary<string, string>? Data { get; init; }

    public static GameEvent Create(
        Guid sessionId,
        DateTimeOffset timestamp,
        string type,
        string title,
        string? details = null,
        GeoPoint? location = null,
        string? actorRoleId = null,
        IReadOnlyDictionary<string, string>? data = null) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = sessionId,
        Timestamp = timestamp,
        Type = type,
        Title = title,
        Details = details,
        Location = location,
        ActorRoleId = actorRoleId,
        Data = data,
    };
}
