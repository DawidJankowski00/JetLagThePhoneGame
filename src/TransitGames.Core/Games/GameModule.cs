using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
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
    public abstract IReadOnlyList<GameRole> Roles { get; }

    /// <summary>Default: every role's location is private.</summary>
    public virtual LocationSharingPolicy LocationSharing => LocationSharingPolicy.None;

    public virtual TSettings CreateDefaultSettings() => new();

    GameSettings IGameModule.CreateDefaultSettings() => CreateDefaultSettings();

    public IGameSession CreateSession(GameSettings settings, string localRoleId)
    {
        if (settings is not TSettings typed)
        {
            throw new ArgumentException($"Expected settings of type {typeof(TSettings).Name}.", nameof(settings));
        }

        if (Roles.All(r => r.Id != localRoleId))
        {
            throw new ArgumentException($"Unknown role '{localRoleId}' for game '{Id}'.", nameof(localRoleId));
        }

        return CreateSession(typed, localRoleId);
    }

    protected abstract TSession CreateSession(TSettings settings, string localRoleId);

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

    public virtual void RegisterServices(IServiceCollection services)
    {
    }
}
