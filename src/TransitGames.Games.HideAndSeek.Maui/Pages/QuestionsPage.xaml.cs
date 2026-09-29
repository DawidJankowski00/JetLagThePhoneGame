using TransitGames.Games.HideAndSeek.Maui.ViewModels;

namespace TransitGames.Games.HideAndSeek.Maui.Pages;

public partial class QuestionsPage : ContentPage
{
    private readonly QuestionsViewModel _viewModel;

    public QuestionsPage(QuestionsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        _viewModel.OnDisappearing();
        base.OnDisappearing();
    }
}
