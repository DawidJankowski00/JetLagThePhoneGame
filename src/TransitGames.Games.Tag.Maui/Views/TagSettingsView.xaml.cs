using TransitGames.Games.Tag.Maui.ViewModels;

namespace TransitGames.Games.Tag.Maui.Views;

public partial class TagSettingsView : ContentView
{
    public TagSettingsView(TagSettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
