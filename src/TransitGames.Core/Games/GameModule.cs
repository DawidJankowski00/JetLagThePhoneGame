using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TransitGames.Core.Events;
using TransitGames.Core.Notifications;
using TransitGames.Core.Sessions;

namespace TransitGames.Core.Games;

/// <summary>Convenience base class that handles settings (de)serialization for a typed game module.</summary>
public abstract class GameModule<TSettings, TSession> : IGameModule
    where TSettings : GameSettings, new()
    where TSession : class, IGameSession
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public abstract string Icon { get; }
    public abstract int MinPlayers { get; }

    /// <summary>Default: offline play is possible.</summary>
    public virtual bool RequiresOnline => false;
    public abstract IReadOnlyList<GameRole> Roles { get; }

    /// <summary>Default: every role's location is private.</summary>
    public virtual LocationSharingPolicy LocationSharing => LocationSharingPolicy.None;

    /// <summary>Default for online location sharing: close to live.</summary>
    public static TimeSpan LiveLocationInterval { get; } = TimeSpan.FromSeconds(20);

    public virtual TSettings CreateDefaultSettings() => new();

    GameSettings IGameModule.CreateDefaultSettings() => CreateDefaultSettings();

    public TimeSpan GetLocationShareInterval(GameSettings settings) => GetLocationShareInterval(Typed(settings));

    protected virtual TimeSpan GetLocationShareInterval(TSettings settings) => LiveLocationInterval;

    public string SerializeSettings(GameSettings settings) =>
        JsonSerializer.Serialize(Typed(settings), GameJson.Options);

    public GameSettings DeserializeSettings(string json) =>
        JsonSerializer.Deserialize<TSettings>(json, GameJson.Options)
        ?? throw new InvalidOperationException("Game settings are empty.");

    public IGameSession CreateSession(GameSettings settings, string localRoleId, Guid? sessionId = null)
    {
        var typed = Typed(settings);
        if (Roles.All(r => r.Id != localRoleId))
        {
            throw new ArgumentException($"Unknown role '{localRoleId}' for game '{Id}'.", nameof(localRoleId));
        }

        return CreateSession(typed, localRoleId, sessionId ?? Guid.NewGuid());
    }

    protected abstract TSession CreateSession(TSettings settings, string localRoleId, Guid sessionId);

    public IGameSession RestoreSession(GameSessionSnapshot snapshot)
    {
        if (snapshot.GameId != Id)
        {
            throw new ArgumentException($"Snapshot belongs to game '{snapshot.GameId}', not '{Id}'.", nameof(snapshot));
        }

        var settings = JsonSerializer.Deserialize<TSettings>(snapshot.SettingsJson, GameJson.Options)
            ?? throw new InvalidOperationException("Snapshot settings are empty.");
        return RestoreSession(snapshot, settings);
    }

    protected abstract TSession RestoreSession(GameSessionSnapshot snapshot, TSettings settings);

    public IReadOnlyList<GameNotification> PlanNotifications(IGameSession session, bool isOnline, DateTimeOffset now) =>
        PlanNotifications(Typed(session), isOnline, now);

    /// <summary>Default: no reminders.</summary>
    protected virtual IReadOnlyList<GameNotification> PlanNotifications(TSession session, bool isOnline, DateTimeOffset now) => [];

    /// <summary>Default: silent.</summary>
    public virtual GameNotification? NotificationFor(GameEvent gameEvent, string localRoleId) => null;

    /// <summary>Default: silent (positions arriving live would be too frequent to notify).</summary>
    public virtual GameNotification? NotificationForLocation(string senderRoleId, string localRoleId) => null;

    public GameResult DescribeResult(IGameSession session, DateTimeOffset now) => DescribeResult(Typed(session), now);

    /// <summary>Default: no result.</summary>
    protected virtual GameResult DescribeResult(TSession session, DateTimeOffset now) => GameResult.Empty;

    public virtual void RegisterServices(IServiceCollection services)
    {
    }

    private static TSession Typed(IGameSession session) =>
        session as TSession
        ?? throw new ArgumentException($"Expected a session of type {typeof(TSession).Name}.", nameof(session));

    private static TSettings Typed(GameSettings settings) =>
        settings as TSettings
        ?? throw new ArgumentException($"Expected settings of type {typeof(TSettings).Name}.", nameof(settings));
}
