namespace TransitGames.Server.Hubs;

/// <summary>Open connections of every device (in memory; empty after a restart).</summary>
public sealed class PresenceTracker
{
    private readonly Dictionary<Guid, HashSet<string>> _connections = [];
    private readonly object _gate = new();

    /// <summary>Returns true when this is the device's first connection.</summary>
    public bool Connected(Guid deviceId, string connectionId)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(deviceId, out var connections))
            {
                _connections[deviceId] = connections = [];
            }

            connections.Add(connectionId);
            return connections.Count == 1;
        }
    }

    /// <summary>Returns true when the device has no connection left.</summary>
    public bool Disconnected(Guid deviceId, string connectionId)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(deviceId, out var connections))
            {
                return true;
            }

            connections.Remove(connectionId);
            if (connections.Count == 0)
            {
                _connections.Remove(deviceId);
                return true;
            }

            return false;
        }
    }

    public IReadOnlyList<string> ConnectionsOf(Guid deviceId)
    {
        lock (_gate)
        {
            return _connections.TryGetValue(deviceId, out var connections) ? [.. connections] : [];
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
