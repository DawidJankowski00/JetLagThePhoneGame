using TransitGames.App.ViewModels;

namespace TransitGames.App.Pages;

public partial class NewGamePage : ContentPage
{
    public NewGamePage(NewGameViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
