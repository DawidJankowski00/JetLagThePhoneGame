using Foundation;
using TransitGames.App.Notifications;
using UIKit;
using UserNotifications;

namespace TransitGames.App;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	private readonly ForegroundNotificationDelegate _notifications = new();

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
	{
		UNUserNotificationCenter.Current.Delegate = _notifications;
		return base.FinishedLaunching(application, launchOptions);
	}
}
