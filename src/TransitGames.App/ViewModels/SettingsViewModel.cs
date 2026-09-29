using CommunityToolkit.Mvvm.ComponentModel;
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
    private bool _initialized;

    public SettingsViewModel(AppPreferences preferences, IDialogService dialogs)
    {
        _preferences = preferences;
        _dialogs = dialogs;

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
        _initialized = true;
    }

    public IReadOnlyList<Option<string>> Languages { get; }

    public IReadOnlyList<Option<ThemePreference>> Themes { get; }

    [ObservableProperty]
    public partial Option<string> SelectedLanguage { get; set; }

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
}
