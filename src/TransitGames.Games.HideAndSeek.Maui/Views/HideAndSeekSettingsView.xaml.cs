using TransitGames.Games.HideAndSeek.Maui.ViewModels;

namespace TransitGames.Games.HideAndSeek.Maui.Views;

public partial class HideAndSeekSettingsView : ContentView
{
    public HideAndSeekSettingsView(HideAndSeekSettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
