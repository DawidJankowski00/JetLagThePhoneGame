using TransitGames.Core.Location;

namespace TransitGames.UI.Settings;

/// <summary>GPS track recording settings (Settings tab), turned into <see cref="TrackingOptions"/>.</summary>
public sealed class TrackingPreferences
{
    public static readonly IReadOnlyList<int> IntervalChoicesSeconds = [1, 3, 5, 10, 30, 60];
    public static readonly IReadOnlyList<int> MinDistanceChoicesMeters = [0, 5, 10, 25, 50];
    public static readonly IReadOnlyList<int> MaxAccuracyChoicesMeters = [20, 35, 50, 100];

    private const string AutoRecordKey = "tracking.autoRecord";
    private const string BatterySaverKey = "tracking.batterySaver";
    private const string IntervalKey = "tracking.intervalSeconds";
    private const string MinDistanceKey = "tracking.minDistanceMeters";
    private const string MaxAccuracyKey = "tracking.maxAccuracyMeters";

    private readonly IPreferences _preferences;

    public TrackingPreferences(IPreferences preferences) => _preferences = preferences;

    /// <summary>Record the device's track automatically while a game is running.</summary>
    public bool AutoRecord
    {
        get => _preferences.Get(AutoRecordKey, true);
        set => _preferences.Set(AutoRecordKey, value);
    }

    public bool BatterySaver
    {
        get => _preferences.Get(BatterySaverKey, false);
        set => _preferences.Set(BatterySaverKey, value);
    }

    public int IntervalSeconds
    {
        get => _preferences.Get(IntervalKey, (int)TrackingOptions.Default.Interval.TotalSeconds);
        set => _preferences.Set(IntervalKey, value);
    }

    public int MinDistanceMeters
    {
        get => _preferences.Get(MinDistanceKey, (int)TrackingOptions.Default.MinDistanceMeters);
        set => _preferences.Set(MinDistanceKey, value);
    }

    public int MaxAccuracyMeters
    {
        get => _preferences.Get(MaxAccuracyKey, (int)TrackingOptions.Default.MaxAccuracyMeters);
        set => _preferences.Set(MaxAccuracyKey, value);
    }

    /// <summary>Battery saver uses the fixed <see cref="TrackingOptions.PowerSaving"/> preset.</summary>
    public TrackingOptions ToOptions() => BatterySaver
        ? TrackingOptions.PowerSaving
        : TrackingOptions.Default with
        {
            Interval = TimeSpan.FromSeconds(IntervalSeconds),
            MinDistanceMeters = MinDistanceMeters,
            MaxAccuracyMeters = MaxAccuracyMeters,
        };
}
