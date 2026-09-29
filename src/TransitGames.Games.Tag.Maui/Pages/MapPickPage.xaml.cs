using System.ComponentModel;
using Mapsui.Layers;
using TransitGames.Games.Tag.Maui.ViewModels;
using TransitGames.UI.Maps;

namespace TransitGames.Games.Tag.Maui.Pages;

public partial class MapPickPage : ContentPage
{
    private readonly MapPickViewModel _viewModel;
    private readonly IMapLayerProvider _layers;
    private readonly MemoryLayer _markersLayer;
    private bool _zoomed;

    public MapPickPage(MapPickViewModel viewModel, IMapLayerProvider layers)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _layers = layers;
        Map.AddLayer(_markersLayer = layers.CreatePointLayer("tag-pick"));
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _layers.SetMarkers(_markersLayer, _viewModel.Markers);
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
        if (e.PropertyName == nameof(MapPickViewModel.Markers))
        {
            _layers.SetMarkers(_markersLayer, _viewModel.Markers);
        }
    }
}
