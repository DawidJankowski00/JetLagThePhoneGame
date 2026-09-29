using TransitGames.Core.Location;

namespace TransitGames.Tracking;

public enum TrackPointDecision
{
    Reject,
    Accept,

    /// <summary>Accept, but there was no usable fix for longer than <see cref="TrackingOptions.SegmentGap"/>.</summary>
    AcceptNewSegment,
}

/// <summary>
/// Decides which fixes are stored: drops inaccurate and out-of-order fixes, thins the stream to the configured
/// interval and minimum distance, and detects signal gaps (metro, tunnels) that start a new segment.
/// Platforms deliver fixes at their own pace (iOS has no interval), so thinning happens here for both.
/// </summary>
public sealed class TrackPointFilter
{
    /// <summary>While standing still, one point per this period keeps the timeline (playback, GPX times) honest.</summary>
    public static readonly TimeSpan StationaryKeepAlive = TimeSpan.FromMinutes(1);

    // Platforms deliver fixes slightly earlier than requested; do not drop every other one because of that.
    private const double IntervalTolerance = 0.9;

    private readonly TrackingOptions _options;
    private LocationSample? _lastAccepted;
    private DateTimeOffset? _lastUsable;

    public TrackPointFilter(TrackingOptions options) => _options = options;

    public TrackPointDecision Evaluate(LocationSample sample)
    {
        if (sample.AccuracyMeters is { } accuracy && accuracy > _options.MaxAccuracyMeters)
        {
            return TrackPointDecision.Reject;
        }

        if (_lastUsable is { } lastUsable && sample.Timestamp <= lastUsable)
        {
            return TrackPointDecision.Reject;
        }

        var gap = _lastUsable is { } previous && sample.Timestamp - previous > _options.SegmentGap;
        _lastUsable = sample.Timestamp;

        if (_lastAccepted is not { } last)
        {
            _lastAccepted = sample;
            return TrackPointDecision.Accept;
        }

        if (gap)
        {
            _lastAccepted = sample;
            return TrackPointDecision.AcceptNewSegment;
        }

        var elapsed = sample.Timestamp - last.Timestamp;
        if (elapsed < _options.Interval * IntervalTolerance)
        {
            return TrackPointDecision.Reject;
        }

        if (GeoMath.DistanceMeters(last.Point, sample.Point) < _options.MinDistanceMeters && elapsed < StationaryKeepAlive)
        {
            return TrackPointDecision.Reject;
        }

        _lastAccepted = sample;
        return TrackPointDecision.Accept;
    }
}
