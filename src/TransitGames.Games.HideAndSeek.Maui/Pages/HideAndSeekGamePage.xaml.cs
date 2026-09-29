using TransitGames.Games.HideAndSeek.Maui.ViewModels;

namespace TransitGames.Games.HideAndSeek.Maui.Pages;

public partial class HideAndSeekGamePage : ContentPage
{
    private readonly HideAndSeekGameViewModel _viewModel;

    public HideAndSeekGamePage(HideAndSeekGameViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        _viewModel.OnDisappearing();
        base.OnDisappearing();
    }
}
