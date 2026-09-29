using TransitGames.Sync;

namespace TransitGames.UI.Settings;

/// <summary>Online mode settings: the game server (reached over a VPN) and the player name shown to others.</summary>
public sealed class OnlinePreferences
{
    private const string ServerUrlKey = "online.server";
    private const string PlayerNameKey = "online.player";

    private readonly IPreferences _preferences;
    private readonly IDeviceInfo _device;

    public OnlinePreferences(IPreferences preferences, IDeviceInfo device)
    {
        _preferences = preferences;
        _device = device;
    }

    /// <summary>Normalized server address ("http://10.8.0.1:5080"), or empty when not configured.</summary>
    public string ServerUrl
    {
        get => _preferences.Get(ServerUrlKey, string.Empty);
        set => _preferences.Set(ServerUrlKey, OnlineServerUrl.Normalize(value) ?? string.Empty);
    }

    public bool IsConfigured => !string.IsNullOrEmpty(ServerUrl);

    public string PlayerName
    {
        get => _preferences.Get(PlayerNameKey, string.Empty) is { Length: > 0 } name ? name : _device.Name;
        set => _preferences.Set(PlayerNameKey, value.Trim());
    }
}
