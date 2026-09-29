using Microsoft.Extensions.DependencyInjection;
using TransitGames.Core.Games;
using TransitGames.Games.HideAndSeek.Maui.Pages;
using TransitGames.Games.HideAndSeek.Maui.Services;
using TransitGames.Games.HideAndSeek.Maui.ViewModels;
using TransitGames.Games.HideAndSeek.Maui.Views;
using TransitGames.UI.Games;

namespace TransitGames.Games.HideAndSeek.Maui;

public sealed class HideAndSeekUiModule : GameUiModule<HideAndSeekModule>
{
    public const string MainRoute = "hideandseek-game";
    public const string AskRoute = "hideandseek-ask";
    public const string QuestionsRoute = "hideandseek-questions";
    public const string CardsRoute = "hideandseek-cards";

    public override string GameId => HideAndSeekModule.GameId;

    public override IReadOnlyList<GameRoute> Routes { get; } =
    [
        new(MainRoute, typeof(HideAndSeekGamePage)),
        new(AskRoute, typeof(AskQuestionPage)),
        new(QuestionsRoute, typeof(QuestionsPage)),
        new(CardsRoute, typeof(CardsPage)),
    ];

    public override string GetGameRoute(string roleId) => MainRoute;

    public override View? CreateSettingsView(GameSettings settings) =>
        settings is HideAndSeekSettings typed ? new HideAndSeekSettingsView(new HideAndSeekSettingsViewModel(typed)) : null;

    protected override void RegisterUiServices(IServiceCollection services)
    {
        services.AddSingleton<HideAndSeekController>();
        services.AddTransient<HideAndSeekGameViewModel>();
        services.AddTransient<AskQuestionViewModel>();
        services.AddTransient<QuestionsViewModel>();
        services.AddTransient<CardsViewModel>();
    }
}
