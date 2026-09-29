using Microsoft.Extensions.DependencyInjection;
using TransitGames.Games.HideAndSeek.Maui.Pages;
using TransitGames.Games.HideAndSeek.Maui.ViewModels;
using TransitGames.UI.Games;

namespace TransitGames.Games.HideAndSeek.Maui;

public sealed class HideAndSeekUiModule : GameUiModule<HideAndSeekModule>
{
    public const string MainRoute = "hideandseek-game";

    public override string GameId => HideAndSeekModule.GameId;

    public override IReadOnlyList<GameRoute> Routes { get; } =
    [
        new(MainRoute, typeof(HideAndSeekGamePage)),
    ];

    public override string GetGameRoute(string roleId) => MainRoute;

    protected override void RegisterUiServices(IServiceCollection services)
    {
        services.AddTransient<HideAndSeekGameViewModel>();
    }
}
