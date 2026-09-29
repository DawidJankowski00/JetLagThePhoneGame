using System.ComponentModel;
using Mapsui.Layers;
using TransitGames.Games.HideAndSeek.Maui.ViewModels;
using TransitGames.UI.Maps;

namespace TransitGames.Games.HideAndSeek.Maui.Pages;

public partial class HideAndSeekGamePage : ContentPage
{
    private readonly HideAndSeekGameViewModel _viewModel;
    private readonly IMapLayerProvider _layers;
    private readonly MemoryLayer _possibleLayer;
    private readonly MemoryLayer _eliminatedLayer;
    private readonly MemoryLayer _hiderZoneLayer;
    private bool _zoomed;

    public HideAndSeekGamePage(HideAndSeekGameViewModel viewModel, IMapLayerProvider layers)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _layers = layers;

        // Game layers sit between the game area and the stations.
        Map.AddLayer(_possibleLayer = layers.CreatePolygonLayer("hs-possible", MapPalette.HidingZone));
        Map.AddLayer(_eliminatedLayer = layers.CreatePolygonLayer("hs-eliminated", MapPalette.EliminatedArea));
        Map.AddLayer(_hiderZoneLayer = layers.CreatePolygonLayer("hs-hider-zone", MapPalette.HidingZone, MapPalette.OwnPosition));
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.OnAppearing();
        if (!_zoomed)
        {
            _zoomed = true;
            Map.ZoomToArea();
        }
    }

    protected override void OnDisappearing()
    {
        _viewModel.OnDisappearing();
        base.OnDisappearing();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HideAndSeekGameViewModel.EliminatedArea):
                _layers.SetPolygon(_eliminatedLayer, _viewModel.EliminatedArea);
                break;
            case nameof(HideAndSeekGameViewModel.PossibleArea):
                _layers.SetPolygon(_possibleLayer, _viewModel.PossibleArea);
                break;
            case nameof(HideAndSeekGameViewModel.HiderZone):
                _layers.SetPolygon(_hiderZoneLayer, _viewModel.HiderZone);
                break;
        }
    }
}
