using TransitGames.App.ViewModels;

namespace TransitGames.App.Pages;

public partial class StationsSetupPage : ContentPage
{
    private readonly StationsSetupViewModel _viewModel;

    public StationsSetupPage(StationsSetupViewModel viewModel)
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
