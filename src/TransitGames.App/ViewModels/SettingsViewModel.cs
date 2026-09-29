using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransitGames.Sync;
using TransitGames.UI.Notifications;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;
using TransitGames.UI.Settings;

namespace TransitGames.App.ViewModels;

public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppPreferences _preferences;
    private readonly IDialogService _dialogs;
    private readonly TrackingPreferences _tracking;
    private readonly OnlinePreferences _online;
    private readonly OnlineServerClient _server;
    private readonly GameNotificationCoordinator _notifications;
    private bool _initialized;

    public SettingsViewModel(
        AppPreferences preferences,
        IDialogService dialogs,
        TrackingPreferences tracking,
        OnlinePreferences online,
        OnlineServerClient server,
        GameNotificationCoordinator notifications)
    {
        _preferences = preferences;
        _dialogs = dialogs;
        _tracking = tracking;
        _online = online;
        _server = server;
        _notifications = notifications;

        Languages =
        [
            new(AppPreferences.LanguageSystem, UiStrings.LanguageSystem),
            new(AppPreferences.LanguagePolish, "Polski"),
            new(AppPreferences.LanguageEnglish, "English"),
        ];
        Themes =
        [
            new(ThemePreference.System, UiStrings.ThemeSystem),
            new(ThemePreference.Light, UiStrings.ThemeLight),
            new(ThemePreference.Dark, UiStrings.ThemeDark),
        ];

        SelectedLanguage = Languages.FirstOrDefault(l => l.Value == preferences.Language) ?? Languages[0];
        SelectedTheme = Themes.FirstOrDefault(t => t.Value == preferences.Theme) ?? Themes[0];

        Intervals = Choices(TrackingPreferences.IntervalChoicesSeconds, UiStrings.SecondsFormat);
        MinDistances = Choices(TrackingPreferences.MinDistanceChoicesMeters, UiStrings.MetersFormat);
        MaxAccuracies = Choices(TrackingPreferences.MaxAccuracyChoicesMeters, UiStrings.MetersFormat);
        AutoRecord = tracking.AutoRecord;
        NotificationsEnabled = preferences.NotificationsEnabled;
        BatterySaver = tracking.BatterySaver;
        SelectedInterval = Closest(Intervals, tracking.IntervalSeconds);
        SelectedMinDistance = Closest(MinDistances, tracking.MinDistanceMeters);
        SelectedMaxAccuracy = Closest(MaxAccuracies, tracking.MaxAccuracyMeters);
        ServerUrl = online.ServerUrl;
        PlayerName = online.PlayerName;
        _initialized = true;
    }

    public IReadOnlyList<Option<string>> Languages { get; }

    public IReadOnlyList<Option<ThemePreference>> Themes { get; }

    public IReadOnlyList<Option<int>> Intervals { get; }

    public IReadOnlyList<Option<int>> MinDistances { get; }

    public IReadOnlyList<Option<int>> MaxAccuracies { get; }

    [ObservableProperty]
    public partial Option<string> SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial bool AutoRecord { get; set; }

    [ObservableProperty]
    public partial bool NotificationsEnabled { get; set; }

    [ObservableProperty]
    public partial string ServerUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PlayerName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnectionResult))]
    public partial string? ConnectionResult { get; set; }

    public bool HasConnectionResult => !string.IsNullOrEmpty(ConnectionResult);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    public partial bool IsTesting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomTracking))]
    public partial bool BatterySaver { get; set; }

    /// <summary>Interval/distance/accuracy apply only without battery saver (which has its own preset).</summary>
    public bool CustomTracking => !BatterySaver;

    [ObservableProperty]
    public partial Option<int> SelectedInterval { get; set; }

    [ObservableProperty]
    public partial Option<int> SelectedMinDistance { get; set; }

    [ObservableProperty]
    public partial Option<int> SelectedMaxAccuracy { get; set; }

    [ObservableProperty]
    public partial Option<ThemePreference> SelectedTheme { get; set; }

    partial void OnSelectedLanguageChanged(Option<string> value)
    {
        if (!_initialized || value is null || value.Value == _preferences.Language)
        {
            return;
        }

        _preferences.Language = value.Value;
        _ = _dialogs.AlertAsync(UiStrings.SettingsLanguage, UiStrings.RestartRequired);
    }

    partial void OnSelectedThemeChanged(Option<ThemePreference> value)
    {
        if (!_initialized || value is null)
        {
            return;
        }

        _preferences.Theme = value.Value;
        _preferences.ApplyTheme(Application.Current!);
    }

    partial void OnAutoRecordChanged(bool value) => Save(() => _tracking.AutoRecord = value);

    partial void OnNotificationsEnabledChanged(bool value) => Save(() =>
    {
        _preferences.NotificationsEnabled = value;
        _notifications.OnPreferenceChanged();
    });

    partial void OnBatterySaverChanged(bool value) => Save(() => _tracking.BatterySaver = value);

    partial void OnSelectedIntervalChanged(Option<int> value) => Save(() => _tracking.IntervalSeconds = value.Value);

    partial void OnSelectedMinDistanceChanged(Option<int> value) => Save(() => _tracking.MinDistanceMeters = value.Value);

    partial void OnSelectedMaxAccuracyChanged(Option<int> value) => Save(() => _tracking.MaxAccuracyMeters = value.Value);

    /// <summary>Saved as soon as it is a valid address (half-typed text is not stored); empty clears it.</summary>
    partial void OnServerUrlChanged(string value)
    {
        ConnectionResult = null;
        if (string.IsNullOrWhiteSpace(value) || OnlineServerUrl.Normalize(value) is not null)
        {
            Save(() => _online.ServerUrl = value);
        }
    }

    partial void OnPlayerNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Save(() => _online.PlayerName = value);
        }
    }

    private bool CanTestConnection() => !IsTesting;

    [RelayCommand(CanExecute = nameof(CanTestConnection))]
    private async Task TestConnectionAsync()
    {
        var url = OnlineServerUrl.Normalize(ServerUrl);
        if (url is null)
        {
            ConnectionResult = UiStrings.SettingsServerInvalid;
            return;
        }

        try
        {
            IsTesting = true;
            ConnectionResult = UiStrings.OnlineConnecting;
            var info = await _server.GetInfoAsync(url);
            ConnectionResult = string.Format(UiStrings.SettingsConnectionOkFormat, url, info.Version);
        }
        catch (OnlineException ex)
        {
            ConnectionResult = OnlineErrors.Describe(ex);
        }
        finally
        {
            IsTesting = false;
        }
    }

    private void Save(Action save)
    {
        if (_initialized)
        {
            save();
        }
    }

    private static IReadOnlyList<Option<int>> Choices(IReadOnlyList<int> values, string format) =>
        values.Select(v => new Option<int>(v, string.Format(format, v))).ToList();

    private static Option<int> Closest(IReadOnlyList<Option<int>> options, int value) =>
        options.MinBy(o => Math.Abs(o.Value - value))!;
}
