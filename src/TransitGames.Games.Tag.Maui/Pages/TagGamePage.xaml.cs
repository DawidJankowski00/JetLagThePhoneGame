using System.ComponentModel;
using Mapsui.Layers;
using TransitGames.Games.Tag.Maui.ViewModels;
using TransitGames.UI.Maps;

namespace TransitGames.Games.Tag.Maui.Pages;

public partial class TagGamePage : ContentPage
{
    private readonly TagGameViewModel _viewModel;
    private readonly IMapLayerProvider _layers;
    private readonly MemoryLayer _zonesLayer;
    private readonly MemoryLayer _targetsLayer;
    private bool _zoomed;

    public TagGamePage(TagGameViewModel viewModel, IMapLayerProvider layers)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _layers = layers;

        // Game layers sit between the game area and the stations.
        Map.AddLayer(_zonesLayer = layers.CreatePolygonLayer("tag-zones", MapPalette.TargetZone, MapPalette.Target, 1));
        Map.AddLayer(_targetsLayer = layers.CreatePointLayer("tag-targets"));
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
            case nameof(TagGameViewModel.Markers):
                _layers.SetMarkers(_targetsLayer, _viewModel.Markers);
                break;
            case nameof(TagGameViewModel.TargetZones):
                _layers.SetPolygon(_zonesLayer, _viewModel.TargetZones);
                break;
        }
    }
}
