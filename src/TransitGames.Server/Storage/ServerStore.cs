using SQLite;

namespace TransitGames.Server.Storage;

public sealed class ServerStorageOptions
{
    public const string Section = "Storage";

    /// <summary>SQLite file; in the Docker image it lives on the /data volume.</summary>
    public string DatabasePath { get; set; } = Path.Combine("data", "transitgames-server.db");

    /// <summary>Games untouched for this long are deleted with their messages.</summary>
    public int RetentionDays { get; set; } = 14;
}

[Table("lobbies")]
internal sealed class LobbyRow
{
    [PrimaryKey]
    public string Code { get; set; } = string.Empty;

    [Indexed(Unique = true)]
    public Guid SessionId { get; set; }

    public string GameId { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string SettingsJson { get; set; } = string.Empty;
    public string RolesJson { get; set; } = "[]";
    public string SharingRulesJson { get; set; } = "[]";
    public long CreatedAtTicks { get; set; }
    public long UpdatedAtTicks { get; set; }
    public bool IsFinished { get; set; }

    /// <summary>Number of role changes so far (see <c>RoleChange</c>).</summary>
    public int RolesVersion { get; set; }
}

[Table("devices")]
internal sealed class DeviceRow
{
    [PrimaryKey]
    public Guid Id { get; set; }

    [Indexed]
    public Guid SessionId { get; set; }

    /// <summary>Current role; its history is in <see cref="DeviceRoleRow"/>.</summary>
    public string RoleId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256 of the device token; the token itself is only known to the device.</summary>
    [Indexed(Unique = true)]
    public string TokenHash { get; set; } = string.Empty;

    public long JoinedAtTicks { get; set; }
}

[Table("messages")]
internal sealed class MessageRow
{
    /// <summary>Global, strictly increasing: the sequence number clients use for catch-up.</summary>
    [PrimaryKey, AutoIncrement]
    public long Sequence { get; set; }

    [Indexed(Unique = true)]
    public Guid MessageId { get; set; }

    [Indexed]
    public Guid SessionId { get; set; }

    public Guid SenderDeviceId { get; set; }
    public string Type { get; set; } = string.Empty;

    /// <summary>Roles allowed to receive the message, as "|role1|role2|" (decided once, at publish time).</summary>
    public string RecipientRoles { get; set; } = string.Empty;

    /// <summary>Roles version at publish time: <see cref="RecipientRoles"/> refers to the roles of that version.</summary>
    public int RolesVersion { get; set; }

    /// <summary>Addressed to every role, so visible to every device whatever its role history.</summary>
    public bool ToAll { get; set; }

    public string Json { get; set; } = string.Empty;
}

/// <summary>The role a device had in a given roles version (a device that joined later has no rows for earlier versions).</summary>
[Table("device_roles")]
internal sealed class DeviceRoleRow
{
    [PrimaryKey, AutoIncrement]
    public long Id { get; set; }

    [Indexed(Name = "device_roles_device_version", Order = 1, Unique = true)]
    public Guid DeviceId { get; set; }

    [Indexed(Name = "device_roles_device_version", Order = 2, Unique = true)]
    public int Version { get; set; }

    [Indexed]
    public Guid SessionId { get; set; }

    public string RoleId { get; set; } = string.Empty;
}

/// <summary>SQLite persistence of the server: games survive a container restart.</summary>
public sealed class ServerStore : IAsyncDisposable
{
    private readonly SQLiteAsyncConnection _connection;
    private readonly Lazy<Task> _init;

    public ServerStore(ServerStorageOptions options)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(options.DatabasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SQLiteAsyncConnection(
            options.DatabasePath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
        _init = new Lazy<Task>(async () =>
        {
            await _connection.EnableWriteAheadLoggingAsync();
            await _connection.CreateTablesAsync<LobbyRow, DeviceRow, MessageRow, DeviceRoleRow>();

            // Devices stored before role history existed: their role at version 0.
            await _connection.ExecuteAsync(
                "INSERT INTO device_roles (DeviceId, Version, SessionId, RoleId) SELECT Id, 0, SessionId, RoleId FROM devices " +
                "WHERE Id NOT IN (SELECT DeviceId FROM device_roles)");
        });
    }

    public async Task<SQLiteAsyncConnection> GetAsync()
    {
        await _init.Value;
        return _connection;
    }

    public async ValueTask DisposeAsync() => await _connection.CloseAsync();
}
