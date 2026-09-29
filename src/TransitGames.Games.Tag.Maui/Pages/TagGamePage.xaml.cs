using System.ComponentModel;
using Mapsui.Layers;
using TransitGames.Games.Tag.Maui.ViewModels;
using TransitGames.UI.Maps;

namespace TransitGames.Games.Tag.Maui.Pages;

public partial class TagGamePage : ContentPage
{
    private readonly TagGameViewModel _viewModel;
    private readonly IMapLayerProvider _layers;
    private readonly MemoryLayer _markersLayer;
    private bool _zoomed;

    public TagGamePage(TagGameViewModel viewModel, IMapLayerProvider layers)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _layers = layers;

        // Start point and destinations sit between the game area and the stations.
        Map.AddLayer(_markersLayer = layers.CreatePointLayer("tag-destinations"));
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
        if (e.PropertyName == nameof(TagGameViewModel.Markers))
        {
            _layers.SetMarkers(_markersLayer, _viewModel.Markers);
        }
    }
}
