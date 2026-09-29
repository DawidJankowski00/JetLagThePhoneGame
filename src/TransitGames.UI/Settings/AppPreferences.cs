using System.Globalization;

namespace TransitGames.UI.Settings;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>User-level app preferences (not tied to a game), stored in platform preferences.</summary>
public sealed class AppPreferences
{
    public const string LanguageSystem = "";
    public const string LanguagePolish = "pl";
    public const string LanguageEnglish = "en";

    private const string LanguageKey = "app.language";
    private const string ThemeKey = "app.theme";

    private readonly IPreferences _preferences;

    public AppPreferences(IPreferences preferences) => _preferences = preferences;

    /// <summary>"" = follow the device (Polish neutral resources, English for English devices).</summary>
    public string Language
    {
        get => _preferences.Get(LanguageKey, LanguageSystem);
        set => _preferences.Set(LanguageKey, value);
    }

    public ThemePreference Theme
    {
        get => Enum.TryParse<ThemePreference>(_preferences.Get(ThemeKey, nameof(ThemePreference.System)), out var t)
            ? t
            : ThemePreference.System;
        set => _preferences.Set(ThemeKey, value.ToString());
    }

    /// <summary>Applies the language before any UI is created (x:Static resources are read at page load).</summary>
    public void ApplyLanguage()
    {
        if (string.IsNullOrEmpty(Language))
        {
            return;
        }

        var culture = CultureInfo.GetCultureInfo(Language);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public void ApplyTheme(Application application) =>
        application.UserAppTheme = Theme switch
        {
            ThemePreference.Light => AppTheme.Light,
            ThemePreference.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };
}
