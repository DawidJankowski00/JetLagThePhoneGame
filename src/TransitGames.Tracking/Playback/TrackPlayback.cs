using TransitGames.Core.Geography;

namespace TransitGames.Tracking.Playback;

/// <summary>
/// Position on a recorded track at a given time: interpolated between fixes of the same segment, held at the
/// last known fix during a gap between segments (no guessing where the metro went).
/// </summary>
public sealed class TrackPlayback
{
    private readonly TrackPoint[] _points;
    private readonly DateTimeOffset[] _times;

    public TrackPlayback(IEnumerable<TrackPoint> points)
    {
        _points = points.OrderBy(p => p.Timestamp).ToArray();
        if (_points.Length == 0)
        {
            throw new ArgumentException("A track without points cannot be played back.", nameof(points));
        }

        _times = _points.Select(p => p.Timestamp).ToArray();

        Segments = _points
            .GroupBy(p => p.Segment)
            .OrderBy(g => g.Key)
            .Select(g => (IReadOnlyList<GeoPoint>)g.Select(p => p.Point).ToList())
            .ToList();
    }

    public DateTimeOffset Start => _points[0].Timestamp;

    public DateTimeOffset End => _points[^1].Timestamp;

    public TimeSpan Duration => End - Start;

    /// <summary>Point lists per segment, for drawing.</summary>
    public IReadOnlyList<IReadOnlyList<GeoPoint>> Segments { get; }

    public GeoPoint PositionAt(DateTimeOffset time)
    {
        if (time <= Start)
        {
            return _points[0].Point;
        }

        if (time >= End)
        {
            return _points[^1].Point;
        }

        // Last point at or before the time (there is one: time > Start), and the one after it.
        var index = Array.BinarySearch(_times, time);
        if (index >= 0)
        {
            return _points[index].Point;
        }

        var before = _points[~index - 1];
        var after = _points[~index];
        if (before.Segment != after.Segment)
        {
            return before.Point;
        }

        var fraction = (time - before.Timestamp) / (after.Timestamp - before.Timestamp);
        return GeoMath.Interpolate(before.Point, after.Point, fraction);
    }

    /// <summary><see cref="PositionAt"/> for a 0..1 position of a time slider.</summary>
    public GeoPoint PositionAtFraction(double fraction) => PositionAt(TimeAtFraction(fraction));

    public DateTimeOffset TimeAtFraction(double fraction) => Start + Duration * Math.Clamp(fraction, 0, 1);

    /// <summary>Inverse of <see cref="TimeAtFraction"/>: 0..1 slider position of a time (clamped to the track).</summary>
    public double FractionAt(DateTimeOffset time) =>
        Duration <= TimeSpan.Zero ? 0 : Math.Clamp((time - Start) / Duration, 0, 1);
}
