using SQLite;
using TransitGames.Core.Sessions;

namespace TransitGames.Core.Persistence;

/// <summary>
/// Upgrades saved sessions of one game after its settings or state changed incompatibly (e.g. a rules rework), so old
/// games still open in the history. Registered by the game module; run once per database by
/// <see cref="SqliteGameSessionStore"/> before it returns any session.
/// </summary>
public interface IGameSessionMigration
{
    /// <summary>Unique, stable id; recorded in the database once the migration ran. Never change it.</summary>
    string Id { get; }

    string GameId { get; }

    /// <summary>The upgraded snapshot, or null when <paramref name="snapshot"/> needs no change.</summary>
    GameSessionSnapshot? Migrate(GameSessionSnapshot snapshot);
}

/// <summary>Migrations already applied to this database (by <see cref="IGameSessionMigration.Id"/>).</summary>
[Table("schema_migrations")]
internal sealed class SchemaMigrationRow
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public long AppliedAtTicks { get; set; }

    /// <summary>How many sessions the migration changed.</summary>
    public int Sessions { get; set; }
}
