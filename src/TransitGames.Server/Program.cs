using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using TransitGames.Core.Games;
using TransitGames.Core.Sync;
using TransitGames.Server;
using TransitGames.Server.Auth;
using TransitGames.Server.Hubs;
using TransitGames.Server.Lobbies;
using TransitGames.Server.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IConfiguration>().GetSection(ServerStorageOptions.Section).Get<ServerStorageOptions>() ?? new ServerStorageOptions());
builder.Services.AddSingleton<ServerStore>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LobbyService>();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddSingleton<SessionGate>();
builder.Services.AddHostedService<RetentionService>();

builder.Services.AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, DeviceTokenAuthenticationHandler>(DeviceTokenAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization();
builder.Services.ConfigureHttpJsonOptions(o => ServerJson.Configure(o.SerializerOptions));
builder.Services.AddSignalR(o =>
    {
        // Game messages are small, but let a question with a few coordinates through comfortably.
        o.MaximumReceiveMessageSize = 256 * 1024;

        // Phones on trains: be tolerant before declaring a client gone.
        o.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        o.KeepAliveInterval = TimeSpan.FromSeconds(15);
    })
    .AddJsonProtocol(o => ServerJson.Configure(o.PayloadSerializerOptions));
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";
app.MapGet("/api/info", () => new ServerInfo("Transit Games", version.Split('+')[0], OnlineProtocol.Version));

var lobbies = app.MapGroup("/api/lobbies").AddEndpointFilter(async (context, next) =>
{
    try
    {
        return await next(context);
    }
    catch (LobbyException ex)
    {
        return Results.Json(ex.ToError(), statusCode: ex.StatusCode);
    }
});

lobbies.MapPost("/", async (CreateLobbyRequest request, LobbyService service) =>
    Results.Ok(await service.CreateAsync(request)));

lobbies.MapGet("/{code}", async (string code, LobbyService service, PresenceTracker presence) =>
    Results.Ok(await service.GetInfoAsync(code, presence.ConnectedDevices)));

lobbies.MapPost("/{code}/join", async (string code, JoinLobbyRequest request, LobbyService service) =>
    Results.Ok(await service.JoinAsync(code, request)));

app.MapHub<GameHub>(OnlineProtocol.HubPath);

app.Run();

/// <summary>Entry point; public for WebApplicationFactory in the integration tests.</summary>
public partial class Program;

namespace TransitGames.Server
{
    /// <summary>Same JSON conventions as the app (camelCase, enums as strings).</summary>
    public static class ServerJson
    {
        public static void Configure(System.Text.Json.JsonSerializerOptions options)
        {
            options.PropertyNamingPolicy = GameJson.Options.PropertyNamingPolicy;
            options.Converters.Add(new JsonStringEnumConverter());
        }
    }

    /// <summary>Periodically deletes games that have not been touched for <see cref="ServerStorageOptions.RetentionDays"/>.</summary>
    public sealed class RetentionService(LobbyService lobbies, ServerStorageOptions options, TimeProvider time, ILogger<RetentionService> logger)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1), time);
            do
            {
                try
                {
                    var deleted = await lobbies.DeleteStaleAsync(time.GetUtcNow().AddDays(-options.RetentionDays));
                    if (deleted > 0)
                    {
                        logger.LogInformation("Deleted {Count} stale games", deleted);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Cleanup of stale games failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}
