using TransitGames.Games.HideAndSeek.Maui.ViewModels;

namespace TransitGames.Games.HideAndSeek.Maui.Pages;

public partial class CardsPage : ContentPage
{
    private readonly CardsViewModel _viewModel;

    public CardsPage(CardsViewModel viewModel)
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
