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
        Routing.RegisterRoute(Routes.NewGameSetup, typeof(GameSetupPage));
        Routing.RegisterRoute(Routes.TrackPreview, typeof(TrackPreviewPage));
        Routing.RegisterRoute(Routes.HistoryDetails, typeof(HistoryDetailsPage));
        Routing.RegisterRoute(Routes.JoinGame, typeof(JoinGamePage));
        games.RegisterRoutes();
    }
}
