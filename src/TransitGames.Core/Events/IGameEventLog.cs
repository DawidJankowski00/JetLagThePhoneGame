namespace TransitGames.Core.Events;

public interface IGameEventLog
{
    event EventHandler<GameEvent>? EventAppended;

    Task AppendAsync(GameEvent gameEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GameEvent>> GetEventsAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
