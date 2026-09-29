namespace TransitGames.Core.Events;

public interface IGameEventLog
{
    event EventHandler<GameEvent>? EventAppended;

    Task AppendAsync(GameEvent gameEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GameEvent>> GetEventsAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Removes the timeline of a session (the game was deleted from the history).</summary>
    Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
