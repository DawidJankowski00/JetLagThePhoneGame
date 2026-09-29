using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TransitGames.App.Services;
using TransitGames.Core.Geography;
using TransitGames.Core.Timing;
using TransitGames.Geo.Areas;
using TransitGames.Geo.Overpass;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;

namespace TransitGames.App.ViewModels;

public enum AreaTool
{
    None,
    Drawing,
    PickingCity,
}

/// <summary>Wizard step: choose the game area (draw a polygon, OSM city boundary, GeoJSON file or a saved area).</summary>
public sealed partial class AreaSetupViewModel : ObservableObject
{
    private readonly NewGameWizard _wizard;
    private readonly BoundaryService _boundaries;
    private readonly IGameAreaRepository _savedAreas;
    private readonly ITextFilePicker _filePicker;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IClock _clock;
    private readonly ILogger<AreaSetupViewModel> _logger;

    public AreaSetupViewModel(
        NewGameWizard wizard,
        BoundaryService boundaries,
        IGameAreaRepository savedAreas,
        ITextFilePicker filePicker,
        IDialogService dialogs,
        INavigationService navigation,
        IClock clock,
        ILogger<AreaSetupViewModel> logger)
    {
        _wizard = wizard;
        _boundaries = boundaries;
        _savedAreas = savedAreas;
        _filePicker = filePicker;
        _dialogs = dialogs;
        _navigation = navigation;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Vertices of the polygon being drawn.</summary>
    public ObservableCollection<GeoPoint> DraftVertices { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial GameArea? Area { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsDrawing), nameof(IsPickingCity), nameof(Hint), nameof(HasHint))]
    public partial AreaTool Tool { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHint), nameof(Hint))]
    public partial string? BusyText { get; set; }

    public string Summary => Area is null
        ? UiStrings.AreaNone
        : string.Format(UiStrings.AreaSummaryFormat, Area.Name, Area.AreaSquareKilometers);

    public bool IsIdle => Tool == AreaTool.None;

    public bool IsDrawing => Tool == AreaTool.Drawing;

    public bool IsPickingCity => Tool == AreaTool.PickingCity;

    public string? Hint => BusyText ?? Tool switch
    {
        AreaTool.Drawing => UiStrings.AreaDrawHint,
        AreaTool.PickingCity => UiStrings.AreaCityHint,
        _ => null,
    };

    public bool HasHint => Hint is not null;

    public void OnAppearing() => Area ??= _wizard.Area;

    [RelayCommand]
    private void StartDrawing()
    {
        DraftVertices.Clear();
        Tool = AreaTool.Drawing;
    }

    [RelayCommand]
    private void UndoVertex()
    {
        if (DraftVertices.Count > 0)
        {
            DraftVertices.RemoveAt(DraftVertices.Count - 1);
        }
    }

    [RelayCommand]
    private async Task FinishDrawingAsync()
    {
        if (DraftVertices.Count < 3)
        {
            await _dialogs.AlertAsync(UiStrings.AreaStepTitle, UiStrings.AreaTooFewPoints);
            return;
        }

        var name = await _dialogs.PromptAsync(UiStrings.AreaStepTitle, UiStrings.AreaNamePrompt, UiStrings.AreaDefaultName);
        if (name is null)
        {
            return;
        }

        try
        {
            SetArea(GameArea.FromVertices(string.IsNullOrWhiteSpace(name) ? UiStrings.AreaDefaultName : name.Trim(), DraftVertices.ToList()));
            DraftVertices.Clear();
            Tool = AreaTool.None;
        }
        catch (ArgumentException)
        {
            await _dialogs.AlertAsync(UiStrings.AreaStepTitle, UiStrings.AreaInvalid);
        }
    }

    [RelayCommand]
    private void CancelTool()
    {
        DraftVertices.Clear();
        Tool = AreaTool.None;
    }

    [RelayCommand]
    private void PickCity() => Tool = AreaTool.PickingCity;

    [RelayCommand]
    private async Task MapTappedAsync(GeoPoint point)
    {
        switch (Tool)
        {
            case AreaTool.Drawing:
                DraftVertices.Add(point);
                break;
            case AreaTool.PickingCity when BusyText is null:
                await PickCityAtAsync(point);
                break;
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var file = await _filePicker.PickAsync(UiStrings.AreaImport);
        if (file is null)
        {
            return;
        }

        try
        {
            var geometry = GeoJson.ReadPolygonal(file.Content);
            SetArea(GameArea.Create(Path.GetFileNameWithoutExtension(file.FileName), geometry, GameAreaSource.ImportedGeoJson));
        }
        catch (GeoJsonFormatException ex)
        {
            await _dialogs.AlertAsync(UiStrings.Error, string.Format(UiStrings.InvalidGeoJsonFormat, ex.Message));
        }
    }

    [RelayCommand]
    private async Task UseSavedAsync()
    {
        var saved = await _savedAreas.ListAsync();
        if (saved.Count == 0)
        {
            await _dialogs.AlertAsync(UiStrings.AreaSaved, UiStrings.AreaNoSaved);
            return;
        }

        var chosen = await _dialogs.ChooseAsync(UiStrings.AreaSaved, saved, s => s.Definition.Name);
        if (chosen is not null)
        {
            SetArea(GameArea.FromDefinition(chosen.Definition));
        }
    }

    private bool CanGoNext() => Area is not null;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        _wizard.Area = Area;
        await _savedAreas.SaveAsync(Area!.Key, Area.ToDefinition(), _clock.UtcNow);
        await _navigation.GoToAsync(Routes.NewGameStations);
    }

    private async Task PickCityAtAsync(GeoPoint point)
    {
        try
        {
            BusyText = UiStrings.Searching;
            var found = await _boundaries.FindAtAsync(point);
            BusyText = null;
            if (found.Count == 0)
            {
                await _dialogs.AlertAsync(UiStrings.AreaCity, UiStrings.AreaCityNone);
                return;
            }

            var boundary = await _dialogs.ChooseAsync(UiStrings.AreaCityChoose, found,
                b => string.Format(UiStrings.AreaCityLevelFormat, b.Name, b.AdminLevel));
            if (boundary is null)
            {
                return;
            }

            BusyText = UiStrings.Downloading;
            SetArea(await _boundaries.DownloadAsync(boundary));
            Tool = AreaTool.None;
        }
        catch (OverpassException ex)
        {
            _logger.LogWarning(ex, "Boundary download failed");
            await _dialogs.AlertAsync(UiStrings.Error, string.Format(UiStrings.NetworkErrorFormat, ex.Message));
        }
        finally
        {
            BusyText = null;
        }
    }

    private void SetArea(GameArea area)
    {
        Area = area;
        _wizard.Area = area;
    }
}
