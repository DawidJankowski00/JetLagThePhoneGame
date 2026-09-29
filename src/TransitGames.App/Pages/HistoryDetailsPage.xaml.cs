using TransitGames.App.ViewModels;

namespace TransitGames.App.Pages;

public partial class HistoryDetailsPage : ContentPage
{
    public HistoryDetailsPage(HistoryDetailsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
