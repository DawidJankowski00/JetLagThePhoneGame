using TransitGames.App.ViewModels;

namespace TransitGames.App.Pages;

public partial class AreaSetupPage : ContentPage
{
    private readonly AreaSetupViewModel _viewModel;

    public AreaSetupPage(AreaSetupViewModel viewModel)
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
