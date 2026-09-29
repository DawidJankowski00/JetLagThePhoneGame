using TransitGames.App.ViewModels;

namespace TransitGames.App.Pages;

public partial class JoinGamePage : ContentPage
{
    private readonly JoinGameViewModel _viewModel;

    public JoinGamePage(JoinGameViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.OnAppearing();
    }
}
