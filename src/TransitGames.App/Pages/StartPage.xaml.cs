using TransitGames.App.ViewModels;

namespace TransitGames.App.Pages;

public partial class StartPage : ContentPage
{
    private readonly StartViewModel _viewModel;

    public StartPage(StartViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}
