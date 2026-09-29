namespace TransitGames.Core.Sessions;

public interface IGameSessionStore
{
    Task SaveAsync(GameSessionSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<GameSessionSnapshot?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>The most recently updated session that is not finished, if any.</summary>
    Task<GameSessionSnapshot?> GetLatestUnfinishedAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GameSessionSnapshot>> ListAsync(CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
