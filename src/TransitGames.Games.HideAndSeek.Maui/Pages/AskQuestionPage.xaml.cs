using TransitGames.Games.HideAndSeek.Maui.ViewModels;

namespace TransitGames.Games.HideAndSeek.Maui.Pages;

public partial class AskQuestionPage : ContentPage
{
    private readonly AskQuestionViewModel _viewModel;
    private bool _zoomed;

    public AskQuestionPage(AskQuestionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
        if (!_zoomed)
        {
            _zoomed = true;
            Map.ZoomToArea();
        }
    }
}
