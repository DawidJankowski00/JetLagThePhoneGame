using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TransitGames.Core.Games;
using TransitGames.Games.Tag.Maui.Pages;
using TransitGames.Games.Tag.Maui.Services;
using TransitGames.Games.Tag.Maui.ViewModels;
using TransitGames.Games.Tag.Maui.Views;
using TransitGames.Geo.Places;
using TransitGames.UI.Games;
using TransitGames.UI.Services;

namespace TransitGames.Games.Tag.Maui;

public sealed class TagUiModule : GameUiModule<TagModule>
{
    public const string MainRoute = "tag-game";
    public const string TargetsRoute = "tag-targets";
    public const string ScoreboardRoute = "tag-scoreboard";
    public const string TargetsEditorRoute = "tag-targets-editor";

    public override string GameId => TagModule.GameId;

    public override IReadOnlyList<GameRoute> Routes { get; } =
    [
        new(MainRoute, typeof(TagGamePage)),
        new(TargetsRoute, typeof(TagTargetsPage)),
        new(ScoreboardRoute, typeof(TagScoreboardPage)),
        new(TargetsEditorRoute, typeof(TargetsEditorPage)),
    ];

    public override string GetGameRoute(string roleId) => MainRoute;

    public override string EventIcon(string eventType) => eventType switch
    {
        TagEventTypes.TargetCaptured => "🎯",
        TagEventTypes.Caught => "🙌",
        TagEventTypes.TimeUp => "⏱️",
        TagEventTypes.ReachedFinal => "🏁",
        TagEventTypes.RoundStarted => "🔄",
        TagEventTypes.Ping => "📍",
        _ => base.EventIcon(eventType),
    };

    public override View? CreateSettingsView(GameSettings settings, IServiceProvider services) =>
        settings is TagSettings typed
            ? new TagSettingsView(new TagSettingsViewModel(
                typed,
                services.GetRequiredService<PlaceCatalog>(),
                services.GetRequiredService<IDialogService>(),
                services.GetRequiredService<INavigationService>(),
                services.GetRequiredService<ILogger<TagSettingsViewModel>>()))
            : null;

    protected override void RegisterUiServices(IServiceCollection services)
    {
        services.AddSingleton<TagController>();
        services.AddTransient<TagGameViewModel>();
        services.AddTransient<TagTargetsViewModel>();
        services.AddTransient<TagScoreboardViewModel>();
        services.AddTransient<TargetsEditorViewModel>();
    }
}
