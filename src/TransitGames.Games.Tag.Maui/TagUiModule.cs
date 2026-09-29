using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TransitGames.Core.Games;
using TransitGames.Games.Tag.Maui.Pages;
using TransitGames.Games.Tag.Maui.Services;
using TransitGames.Games.Tag.Maui.ViewModels;
using TransitGames.Games.Tag.Maui.Views;
using TransitGames.UI.Games;
using TransitGames.UI.Services;

namespace TransitGames.Games.Tag.Maui;

public sealed class TagUiModule : GameUiModule<TagModule>
{
    public const string MainRoute = "tag-game";
    public const string ResultsRoute = "tag-results";
    public const string MapPickRoute = "tag-map-pick";

    public override string GameId => TagModule.GameId;

    public override IReadOnlyList<GameRoute> Routes { get; } =
    [
        new(MainRoute, typeof(TagGamePage)),
        new(ResultsRoute, typeof(TagResultsPage)),
        new(MapPickRoute, typeof(MapPickPage)),
    ];

    public override string GetGameRoute(string roleId) => MainRoute;

    public override string EventIcon(string eventType) => eventType switch
    {
        TagEventTypes.GameStarted => "🚦",
        TagEventTypes.TurnStarted => "🔄",
        TagEventTypes.Caught => "🙌",
        TagEventTypes.CatchRequested => "✋",
        TagEventTypes.RideStarted => "🚌",
        TagEventTypes.RideEnded => "🛑",
        TagEventTypes.CardDrawn => "🃏",
        TagEventTypes.ChallengeCompleted => "✅",
        TagEventTypes.ChallengeAbandoned => "✖️",
        TagEventTypes.Arrived => "🏁",
        TagEventTypes.TimeUp => "⏱️",
        TagEventTypes.GameEnded => "🏆",
        TagEventTypes.CoinsAdjusted => "🪙",
        _ => base.EventIcon(eventType),
    };

    public override View? CreateSettingsView(GameSettings settings, IServiceProvider services) =>
        settings is TagSettings typed
            ? new TagSettingsView(new TagSettingsViewModel(
                typed,
                services.GetRequiredService<IDialogService>(),
                services.GetRequiredService<ITextFilePicker>(),
                services.GetRequiredService<ILogger<TagSettingsViewModel>>()))
            : null;

    protected override void RegisterUiServices(IServiceCollection services)
    {
        services.AddSingleton<TagController>();
        services.AddTransient<TagGameViewModel>();
        services.AddTransient<TagResultsViewModel>();
        services.AddTransient<MapPickViewModel>();
    }
}
