using TransitGames.App.Pages;
using TransitGames.UI.Games;

namespace TransitGames.App;

public partial class AppShell : Shell
{
    public AppShell(GameUiRegistry games)
    {
        InitializeComponent();
        Routing.RegisterRoute(Routes.NewGame, typeof(NewGamePage));
        games.RegisterRoutes();
    }
}
