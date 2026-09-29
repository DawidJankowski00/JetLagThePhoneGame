using System.Collections.Concurrent;
using SQLite;

namespace TransitGames.Core.Persistence;

public sealed record DatabaseOptions(string DatabasePath);

/// <summary>
/// Single shared SQLite connection. Tables are created lazily by the repositories that own them,
/// so modules can add their own tables without touching this class.
/// </summary>
public sealed class AppDatabase : IAsyncDisposable
{
    private const SQLiteOpenFlags Flags =
        SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache | SQLiteOpenFlags.FullMutex;

    private readonly SQLiteAsyncConnection _connection;
    private readonly ConcurrentDictionary<Type, Lazy<Task>> _tables = new();

    public AppDatabase(DatabaseOptions options)
    {
        var directory = Path.GetDirectoryName(options.DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SQLiteAsyncConnection(options.DatabasePath, Flags);
    }

    /// <summary>Returns the connection after making sure the table for <typeparamref name="T"/> exists.</summary>
    public async Task<SQLiteAsyncConnection> GetAsync<T>()
        where T : new()
    {
        await _tables.GetOrAdd(typeof(T), _ => new Lazy<Task>(() => _connection.CreateTableAsync<T>())).Value
            .ConfigureAwait(false);
        return _connection;
    }

    public async ValueTask DisposeAsync() => await _connection.CloseAsync().ConfigureAwait(false);
}
