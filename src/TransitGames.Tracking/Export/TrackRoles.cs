using TransitGames.Core.Events;

namespace TransitGames.Tracking.Export;

/// <summary>A run of consecutive fixes recorded in one segment while the device had one role (null: unknown / none).</summary>
public sealed record RolePiece(string? Role, IReadOnlyList<TrackPoint> Points);

/// <summary>
/// Splits a track by the role the device had (games whose roles change, e.g. Tag: runner, then chaser after a catch).
/// The role at a moment is the one of the last event carrying <see cref="GameEventData.LocalRole"/> before it, or
/// the track's own role before the first such event.
/// </summary>
public static class TrackRoles
{
    public static IReadOnlyList<RolePiece> Split(TrackWithPoints track, IReadOnlyList<GameEvent> events)
    {
        var changes = events
            .Where(e => e.Data?.ContainsKey(GameEventData.LocalRole) == true)
            .OrderBy(e => e.Timestamp)
            .Select(e => (At: e.Timestamp, Role: NullIfEmpty(e.Data![GameEventData.LocalRole])))
            .ToList();

        var pieces = new List<RolePiece>();
        List<TrackPoint>? current = null;
        (int Segment, string? Role)? currentKey = null;
        foreach (var point in track.Points.OrderBy(p => p.Segment).ThenBy(p => p.Timestamp))
        {
            var last = changes.FindLastIndex(c => c.At <= point.Timestamp);
            var role = last >= 0 ? changes[last].Role : NullIfEmpty(track.Track.RoleId);
            var key = (point.Segment, role);
            if (current is null || currentKey != key)
            {
                current = [];
                currentKey = key;
                pieces.Add(new RolePiece(role, current));
            }

            current.Add(point);
        }

        return pieces;
    }

    /// <summary>Whether the track has any role information worth writing.</summary>
    public static bool HasRoles(IReadOnlyList<RolePiece> pieces) => pieces.Any(p => p.Role is not null);

    private static string? NullIfEmpty(string? role) => string.IsNullOrEmpty(role) ? null : role;
}
