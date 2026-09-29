using System.Collections.Concurrent;

namespace TransitGames.Server.Hubs;

/// <summary>
/// Serializes publishing and connecting within one game, so a role change and the group moves it causes are never
/// interleaved with the delivery of another message of that game.
/// </summary>
public sealed class SessionGate
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> EnterAsync(Guid sessionId)
    {
        var semaphore = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
