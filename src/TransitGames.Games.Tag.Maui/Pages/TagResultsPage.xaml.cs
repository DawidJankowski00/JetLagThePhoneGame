using TransitGames.Games.Tag.Maui.ViewModels;

namespace TransitGames.Games.Tag.Maui.Pages;

public partial class TagResultsPage : ContentPage
{
    private readonly TagResultsViewModel _viewModel;

    public TagResultsPage(TagResultsViewModel viewModel)
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
