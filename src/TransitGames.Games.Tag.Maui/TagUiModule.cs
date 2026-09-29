using Microsoft.Extensions.DependencyInjection;
using TransitGames.Games.Tag.Maui.Pages;
using TransitGames.Games.Tag.Maui.ViewModels;
using TransitGames.UI.Games;

namespace TransitGames.Games.Tag.Maui;

public sealed class TagUiModule : GameUiModule<TagModule>
{
    public const string MainRoute = "tag-game";

    public override string GameId => TagModule.GameId;

    public override IReadOnlyList<GameRoute> Routes { get; } =
    [
        new(MainRoute, typeof(TagGamePage)),
    ];

    public override string GetGameRoute(string roleId) => MainRoute;

    protected override void RegisterUiServices(IServiceCollection services)
    {
        services.AddTransient<TagGameViewModel>();
    }
}
