using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransitGames.Core.Geography;
using TransitGames.Core.Stations;
using TransitGames.Games.Tag.Resources;
using TransitGames.Games.Tag.Targets;
using TransitGames.Geo.Areas;
using TransitGames.UI.Maps;
using TransitGames.UI.Services;

namespace TransitGames.Games.Tag.Maui.ViewModels;

/// <summary>
/// Targets on the map before the game: a tap adds a target, a tap on a target removes it; in "finish" mode a tap
/// places the final target. Edits the <see cref="TagSettingsViewModel"/> it was opened from.
/// </summary>
public sealed partial class TargetsEditorViewModel : ObservableObject, IQueryAttributable
{
    public const string SettingsParameter = "tagSettings";

    /// <summary>A tap closer than this to a target selects it instead of adding a new one.</summary>
    private const double HitMeters = 120;

    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private TagSettingsViewModel? _editor;

    public TargetsEditorViewModel(IDialogService dialogs, INavigationService navigation)
    {
        _dialogs = dialogs;
        _navigation = navigation;
    }

    [ObservableProperty]
    public partial GameArea? Area { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<Station> Stations { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<MapMarker> Markers { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeText))]
    public partial bool IsFinalMode { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    public string ModeText => IsFinalMode ? TagStrings.EditorModeFinal : TagStrings.EditorModeTargets;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(SettingsParameter, out var value) && value is TagSettingsViewModel editor)
        {
            _editor = editor;
            Area = editor.Area;
            Stations = editor.Settings.Stations;
            Refresh();
        }
    }

    [RelayCommand]
    private async Task MapTappedAsync(GeoPoint point)
    {
        if (_editor is not { } editor)
        {
            return;
        }

        if (IsFinalMode)
        {
            editor.SetFinalTarget(point);
            Refresh();
            return;
        }

        var hit = editor.BaseTargets
            .Select(t => (Target: t, Distance: t.Location.DistanceTo(point)))
            .Where(x => x.Distance <= HitMeters)
            .OrderBy(x => x.Distance)
            .Select(x => x.Target)
            .FirstOrDefault();
        if (hit is not null)
        {
            if (await _dialogs.ConfirmAsync(TagStrings.EditorRemoveTitle, string.Format(TagStrings.EditorRemoveFormat, hit.Name)))
            {
                editor.SetBaseTargets(editor.BaseTargets.Where(t => t != hit));
                Refresh();
            }

            return;
        }

        if (Area is { } area && !area.Contains(point))
        {
            await _dialogs.AlertAsync(TagStrings.EditorTitle, TagStrings.EditorOutsideArea);
            return;
        }

        var name = await _dialogs.PromptAsync(
            TagStrings.EditorAddTitle,
            TagStrings.EditorAddName,
            string.Format(TagStrings.TargetDefaultName, editor.BaseTargets.Count + 1));
        if (name is null)
        {
            return;
        }

        editor.SetBaseTargets([.. editor.BaseTargets, TargetGenerator.Manual(point, string.IsNullOrWhiteSpace(name) ? string.Format(TagStrings.TargetDefaultName, editor.BaseTargets.Count + 1) : name.Trim())]);
        Refresh();
    }

    [RelayCommand]
    private void ToggleMode() => IsFinalMode = !IsFinalMode;

    [RelayCommand]
    private void RemoveFinal()
    {
        _editor?.SetFinalTarget(null);
        Refresh();
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        if (_editor is not null && await _dialogs.ConfirmAsync(TagStrings.EditorTitle, TagStrings.EditorClearConfirm))
        {
            _editor.SetBaseTargets([]);
            Refresh();
        }
    }

    [RelayCommand]
    private Task DoneAsync() => _navigation.GoBackAsync();

    private void Refresh()
    {
        if (_editor is not { } editor)
        {
            return;
        }

        var markers = editor.BaseTargets.Select(t => new MapMarker(t.Location, MapPalette.Target, t.Name, 0.55)).ToList();
        if (editor.Settings.FinalTarget is { } final)
        {
            markers.Add(new MapMarker(final.Location, MapPalette.FinalTarget, final.Name, 0.7));
        }

        Markers = markers;
        SummaryText = editor.TargetsSummary;
    }
}
