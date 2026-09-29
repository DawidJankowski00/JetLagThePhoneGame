using TransitGames.App.ViewModels;

namespace TransitGames.App.Pages;

public partial class TracksPage : ContentPage
{
    private readonly TracksViewModel _viewModel;

    public TracksPage(TracksViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        _viewModel.OnDisappearing();
        base.OnDisappearing();
    }
}
