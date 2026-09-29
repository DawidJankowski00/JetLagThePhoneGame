using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TransitGames.App.Services;
using TransitGames.Core.Geography;
using TransitGames.Core.Sessions;
using TransitGames.Core.Stations;
using TransitGames.Geo.Areas;
using TransitGames.Geo.Overpass;
using TransitGames.Geo.Stations;
using TransitGames.UI.Games;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;

namespace TransitGames.App.ViewModels;

/// <summary>A row in the station list.</summary>
public sealed partial class StationItem : ObservableObject
{
    public StationItem(Station station)
    {
        Station = station;
        IsEnabled = station.IsEnabled;
    }

    public Station Station { get; private set; }

    public string Name => Station.Name;

    public string Details => string.Join(" · ", ModeNames(Station.Modes).Concat(Station.IsCustom ? [UiStrings.StationCustom] : []));

    public bool IsCustom => Station.IsCustom;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    public void SetEnabled(bool isEnabled)
    {
        Station = Station with { IsEnabled = isEnabled };
        IsEnabled = isEnabled;
    }

    public static IEnumerable<string> ModeNames(TransitModes modes)
    {
        if (modes.HasFlag(TransitModes.Rail)) yield return UiStrings.ModeRail;
        if (modes.HasFlag(TransitModes.Subway)) yield return UiStrings.ModeSubway;
        if (modes.HasFlag(TransitModes.Tram)) yield return UiStrings.ModeTram;
        if (modes.HasFlag(TransitModes.Bus)) yield return UiStrings.ModeBus;
    }
}

/// <summary>Wizard step: download/cache stations of the area, enable/disable them, add custom ones, start the game.</summary>
public sealed partial class StationsSetupViewModel : ObservableObject
{
    private readonly NewGameWizard _wizard;
    private readonly StationCatalog _catalog;
    private readonly GameSessionManager _sessions;
    private readonly GameUiRegistry _uiRegistry;
    private readonly ITextFilePicker _filePicker;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly ILogger<StationsSetupViewModel> _logger;

    public StationsSetupViewModel(
        NewGameWizard wizard,
        StationCatalog catalog,
        GameSessionManager sessions,
        GameUiRegistry uiRegistry,
        ITextFilePicker filePicker,
        IDialogService dialogs,
        INavigationService navigation,
        ILogger<StationsSetupViewModel> logger)
    {
        _wizard = wizard;
        _catalog = catalog;
        _sessions = sessions;
        _uiRegistry = uiRegistry;
        _filePicker = filePicker;
        _dialogs = dialogs;
        _navigation = navigation;
        _logger = logger;
    }

    public ObservableCollection<StationItem> Items { get; } = [];

    [ObservableProperty]
    public partial GameArea? Area { get; set; }

    /// <summary>Snapshot of <see cref="Items"/> for the map (replaced on every change).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Station> MapStations { get; set; } = [];

    [ObservableProperty]
    public partial bool UseRail { get; set; } = true;

    [ObservableProperty]
    public partial bool UseSubway { get; set; } = true;

    [ObservableProperty]
    public partial bool UseTram { get; set; } = true;

    [ObservableProperty]
    public partial bool UseBus { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Hint), nameof(HasHint))]
    public partial bool IsAddingCustom { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Hint), nameof(HasHint), nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(StartCommand))]
    public partial string? BusyText { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string CountText { get; set; } = string.Empty;

    public bool IsIdle => BusyText is null;

    public string? Hint => BusyText ?? (IsAddingCustom ? UiStrings.StationsAddHint : null);

    public bool HasHint => Hint is not null;

    private TransitModes SelectedModes =>
        (UseRail ? TransitModes.Rail : 0) | (UseSubway ? TransitModes.Subway : 0) |
        (UseTram ? TransitModes.Tram : 0) | (UseBus ? TransitModes.Bus : 0);

    public async Task OnAppearingAsync()
    {
        var area = _wizard.RequireArea();
        if (Area?.Key == area.Key)
        {
            return;
        }

        Area = area;
        Message = null;
        var cached = await _catalog.GetCachedAsync(area);
        SetStations(cached);
        if (cached.Count == 0)
        {
            await DownloadAsync();
        }
    }

    private bool CanDownload() => BusyText is null;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        if (Area is null)
        {
            return;
        }

        if (SelectedModes == TransitModes.None)
        {
            await _dialogs.AlertAsync(UiStrings.StationsStepTitle, UiStrings.StationsSelectMode);
            return;
        }

        try
        {
            BusyText = UiStrings.Downloading;
            var result = await _catalog.DownloadAsync(Area, SelectedModes);
            SetStations(result.Stations);
            Message = string.Format(UiStrings.StationsDownloadedFormat, result.Stations.Count(s => !s.IsCustom), result.Merged);
        }
        catch (OverpassException ex)
        {
            _logger.LogWarning(ex, "Station download failed");
            await _dialogs.AlertAsync(UiStrings.Error, string.Format(UiStrings.NetworkErrorFormat, ex.Message));
        }
        finally
        {
            BusyText = null;
        }
    }

    [RelayCommand]
    private void ToggleAddCustom() => IsAddingCustom = !IsAddingCustom;

    [RelayCommand]
    private async Task MapTappedAsync(GeoPoint point)
    {
        if (!IsAddingCustom || Area is null)
        {
            return;
        }

        if (!Area.Contains(point))
        {
            await _dialogs.AlertAsync(UiStrings.StationsStepTitle, UiStrings.StationOutsideArea);
            return;
        }

        var name = await _dialogs.PromptAsync(UiStrings.StationsStepTitle, UiStrings.StationNamePrompt);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await _catalog.AddCustomAsync(Area, name, point);
        IsAddingCustom = false;
        await ReloadAsync();
    }

    [RelayCommand]
    private Task StationTappedAsync(Station station)
    {
        var item = Items.FirstOrDefault(i => i.Station.Id == station.Id);
        return item is null ? Task.CompletedTask : ToggleAsync(item);
    }

    [RelayCommand]
    private async Task ToggleAsync(StationItem item)
    {
        if (Area is null)
        {
            return;
        }

        item.SetEnabled(!item.IsEnabled);
        await _catalog.SetEnabledAsync(Area, item.Station, item.IsEnabled);
        RefreshSummary();
    }

    [RelayCommand]
    private async Task DeleteCustomAsync(StationItem item)
    {
        if (Area is null || !item.IsCustom)
        {
            return;
        }

        await _catalog.DeleteCustomAsync(Area, item.Station);
        Items.Remove(item);
        RefreshSummary();
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (Area is null)
        {
            return;
        }

        var file = await _filePicker.PickAsync(UiStrings.StationsImport);
        if (file is null)
        {
            return;
        }

        try
        {
            var result = await _catalog.ImportGeoJsonAsync(Area, file.Content);
            Message = string.Format(UiStrings.StationsImportedFormat, result.Stations.Count, result.SkippedOutsideArea, result.SkippedWithoutName);
            await ReloadAsync();
        }
        catch (GeoJsonFormatException ex)
        {
            await _dialogs.AlertAsync(UiStrings.Error, string.Format(UiStrings.InvalidGeoJsonFormat, ex.Message));
        }
    }

    private bool CanStart() => BusyText is null && Items.Any(i => i.IsEnabled);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var module = _wizard.RequireModule();
        var area = _wizard.RequireArea();
        var enabled = Items.Where(i => i.IsEnabled).Select(i => i.Station).ToList();
        if (enabled.Count == 0 || _wizard.Role is not { } role)
        {
            await _dialogs.AlertAsync(UiStrings.Error, UiStrings.StationsNoneEnabled);
            return;
        }

        var settings = module.CreateDefaultSettings();
        settings.Area = area.ToDefinition();
        settings.Stations = enabled;
        var session = await _sessions.StartNewAsync(module.Id, settings, role.Id);

        // Absolute route: the wizard pages are dropped and Back from the game returns to the start page.
        await _navigation.GoToAsync($"//{Routes.Start}/{_uiRegistry.Get(session.GameId).GetGameRoute(session.LocalRoleId)}");
    }

    private async Task ReloadAsync()
    {
        if (Area is not null)
        {
            SetStations(await _catalog.GetCachedAsync(Area));
        }
    }

    private void SetStations(IReadOnlyList<Station> stations)
    {
        Items.Clear();
        foreach (var station in stations)
        {
            Items.Add(new StationItem(station));
        }

        RefreshSummary();
    }

    private void RefreshSummary()
    {
        MapStations = Items.Select(i => i.Station).ToList();
        CountText = Items.Count == 0
            ? UiStrings.StationsEmpty
            : string.Format(UiStrings.StationsCountFormat, Items.Count(i => i.IsEnabled), Items.Count);
    }
}
