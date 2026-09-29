namespace TransitGames.Server.Hubs;

/// <summary>Which devices currently have at least one open connection (in memory; empty after a restart).</summary>
public sealed class PresenceTracker
{
    private readonly Dictionary<Guid, int> _connections = [];
    private readonly object _gate = new();

    /// <summary>Returns true when this is the device's first connection.</summary>
    public bool Connected(Guid deviceId)
    {
        lock (_gate)
        {
            var count = _connections.GetValueOrDefault(deviceId) + 1;
            _connections[deviceId] = count;
            return count == 1;
        }
    }

    /// <summary>Returns true when the device has no connection left.</summary>
    public bool Disconnected(Guid deviceId)
    {
        lock (_gate)
        {
            var count = _connections.GetValueOrDefault(deviceId) - 1;
            if (count <= 0)
            {
                _connections.Remove(deviceId);
                return true;
            }

            _connections[deviceId] = count;
            return false;
        }
    }

    public IReadOnlyCollection<Guid> ConnectedDevices
    {
        get
        {
            lock (_gate)
            {
                return [.. _connections.Keys];
            }
        }
    }
}
