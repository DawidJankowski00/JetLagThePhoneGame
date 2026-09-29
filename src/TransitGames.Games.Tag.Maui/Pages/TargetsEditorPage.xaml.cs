using System.ComponentModel;
using Mapsui.Layers;
using TransitGames.Games.Tag.Maui.ViewModels;
using TransitGames.UI.Maps;

namespace TransitGames.Games.Tag.Maui.Pages;

public partial class TargetsEditorPage : ContentPage
{
    private readonly TargetsEditorViewModel _viewModel;
    private readonly IMapLayerProvider _layers;
    private readonly MemoryLayer _targetsLayer;

    public TargetsEditorPage(TargetsEditorViewModel viewModel, IMapLayerProvider layers)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _layers = layers;
        Map.AddLayer(_targetsLayer = layers.CreatePointLayer("tag-editor-targets"));
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TargetsEditorViewModel.Markers))
        {
            _layers.SetMarkers(_targetsLayer, _viewModel.Markers);
        }
    }
}
