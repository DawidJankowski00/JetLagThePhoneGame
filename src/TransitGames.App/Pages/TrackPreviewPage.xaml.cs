using TransitGames.App.ViewModels;

namespace TransitGames.App.Pages;

public partial class TrackPreviewPage : ContentPage
{
    private readonly TrackPreviewViewModel _viewModel;

    public TrackPreviewPage(TrackPreviewViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnDisappearing()
    {
        _viewModel.OnDisappearing();
        base.OnDisappearing();
    }
}
