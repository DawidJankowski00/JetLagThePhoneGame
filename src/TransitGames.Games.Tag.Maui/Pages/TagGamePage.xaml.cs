using TransitGames.Games.Tag.Maui.ViewModels;

namespace TransitGames.Games.Tag.Maui.Pages;

public partial class TagGamePage : ContentPage
{
    private readonly TagGameViewModel _viewModel;

    public TagGamePage(TagGameViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        _viewModel.OnDisappearing();
        base.OnDisappearing();
    }
}
