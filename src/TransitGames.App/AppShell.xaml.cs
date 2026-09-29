using TransitGames.App.Pages;
using TransitGames.UI.Games;

namespace TransitGames.App;

public partial class AppShell : Shell
{
    public AppShell(GameUiRegistry games)
    {
        InitializeComponent();
        Routing.RegisterRoute(Routes.NewGame, typeof(NewGamePage));
        Routing.RegisterRoute(Routes.NewGameArea, typeof(AreaSetupPage));
        Routing.RegisterRoute(Routes.NewGameStations, typeof(StationsSetupPage));
        Routing.RegisterRoute(Routes.TrackPreview, typeof(TrackPreviewPage));
        games.RegisterRoutes();
    }
}
