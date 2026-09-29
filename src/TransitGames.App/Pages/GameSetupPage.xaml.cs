using TransitGames.App.ViewModels;
using TransitGames.UI.Resources;

namespace TransitGames.App.Pages;

public partial class GameSetupPage : ContentPage
{
    private readonly GameSetupViewModel _viewModel;

    public GameSetupPage(GameSetupViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SettingsHost.Content ??= _viewModel.CreateSettingsView()
            ?? new Label { Text = UiStrings.GameSetupNoOptions, Style = (Style)Application.Current!.Resources["Caption"] };
    }
}
