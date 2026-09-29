using TransitGames.Core.Geography;

namespace TransitGames.Core.Location;

/// <summary>A single GPS fix.</summary>
public sealed record LocationSample(
    GeoPoint Point,
    DateTimeOffset Timestamp,
    double? AccuracyMeters = null,
    double? AltitudeMeters = null,
    double? SpeedMetersPerSecond = null);

/// <summary>Continuous location tracking settings.</summary>
public sealed record TrackingOptions
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(5);

    public double MinDistanceMeters { get; init; } = 10;

    /// <summary>Fixes with a worse horizontal accuracy are discarded.</summary>
    public double MaxAccuracyMeters { get; init; } = 50;

    /// <summary>Lower accuracy / longer interval to save battery.</summary>
    public bool BatterySaver { get; init; }

    /// <summary>No usable fix for this long (metro tunnel, GPS off) starts a new track segment.</summary>
    public TimeSpan SegmentGap { get; init; } = TimeSpan.FromMinutes(2);

    public static TrackingOptions Default { get; } = new();

    public static TrackingOptions PowerSaving { get; } = new()
    {
        Interval = TimeSpan.FromSeconds(20),
        MinDistanceMeters = 30,
        MaxAccuracyMeters = 100,
        BatterySaver = true,
        SegmentGap = TimeSpan.FromMinutes(3),
    };
}

/// <summary>Platform GPS access. Implemented in the MAUI layer (foreground service on Android).</summary>
public interface ILocationTracker
{
    bool IsTracking { get; }

    LocationSample? LastLocation { get; }

    event EventHandler<LocationSample>? LocationChanged;

    /// <summary>One-shot fix, e.g. at the moment a question is asked.</summary>
    Task<LocationSample?> GetCurrentLocationAsync(CancellationToken cancellationToken = default);

    Task StartAsync(TrackingOptions options, CancellationToken cancellationToken = default);

    Task StopAsync();
}
