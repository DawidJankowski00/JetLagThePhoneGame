using TransitGames.Games.Tag.Maui.ViewModels;

namespace TransitGames.Games.Tag.Maui.Pages;

public partial class TagScoreboardPage : ContentPage
{
    private readonly TagScoreboardViewModel _viewModel;

    public TagScoreboardPage(TagScoreboardViewModel viewModel)
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
