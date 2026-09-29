using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TransitGames.Core.Sync;
using TransitGames.Server.Auth;
using TransitGames.Server.Lobbies;

namespace TransitGames.Server.Hubs;

/// <summary>
/// Real-time channel of one device in one game. Every connection joins the group of its game and of its current
/// role; a message is delivered only to the groups of the roles decided by <see cref="LobbyService.RecipientRoles"/>.
/// The sender's connection is excluded: clients echo their own messages locally.
/// When a message changes roles, the connections are moved to their new role groups before anything else of that
/// game is delivered (<see cref="SessionGate"/>), so no message reaches a device under a role it no longer has.
/// </summary>
[Authorize(AuthenticationSchemes = DeviceTokenAuthenticationHandler.SchemeName)]
public sealed class GameHub(LobbyService lobbies, PresenceTracker presence, SessionGate gate, ILogger<GameHub> logger) : Hub
{
    public static string SessionGroup(Guid sessionId) => $"session:{sessionId}";

    public static string RoleGroup(Guid sessionId, string roleId) => $"session:{sessionId}:role:{roleId}";

    private DeviceIdentity Device => DeviceTokenAuthenticationHandler.GetDevice(Context.User!);

    public override async Task OnConnectedAsync()
    {
        var authenticated = Device;
        using (await gate.EnterAsync(authenticated.SessionId))
        {
            // The role may have changed since the token was checked.
            var device = await lobbies.GetDeviceAsync(authenticated.DeviceId) ?? authenticated;
            await Groups.AddToGroupAsync(Context.ConnectionId, SessionGroup(device.SessionId));
            await Groups.AddToGroupAsync(Context.ConnectionId, RoleGroup(device.SessionId, device.RoleId));
            presence.Connected(device.DeviceId, Context.ConnectionId);
            logger.LogInformation("Device {Device} ({Role}) connected to {Session}", device.DeviceId, device.RoleId, device.SessionId);
        }

        await BroadcastPeersAsync(authenticated.SessionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var device = Device;
        if (presence.Disconnected(device.DeviceId, Context.ConnectionId))
        {
            logger.LogInformation("Device {Device} disconnected from {Session}", device.DeviceId, device.SessionId);
            await BroadcastPeersAsync(device.SessionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Stores and routes a message. Idempotent: a retried message is stored and delivered once.</summary>
    public async Task<long> Publish(SyncMessage message)
    {
        var sessionId = Device.SessionId;
        PublishResult result;
        using (await gate.EnterAsync(sessionId))
        {
            try
            {
                result = await lobbies.PublishAsync(Device, message);
            }
            catch (LobbyException ex)
            {
                throw new HubException(ex.Message);
            }

            if (result.IsNew)
            {
                foreach (var role in result.RecipientRoles)
                {
                    await Clients.GroupExcept(RoleGroup(sessionId, role), [Context.ConnectionId])
                        .SendAsync(OnlineProtocol.ReceiveMethod, result.Message);
                }

                await MoveConnectionsAsync(sessionId, result.RoleMoves);
            }
        }

        if (result.RoleMoves.Count > 0)
        {
            logger.LogInformation("Roles changed in {Session}", sessionId);
            await BroadcastPeersAsync(sessionId);
        }

        return result.Message.Sequence;
    }

    /// <summary>Catch-up after (re)connecting: messages for this device after <paramref name="afterSequence"/>.</summary>
    public Task<IReadOnlyList<SequencedMessage>> Sync(long afterSequence) => lobbies.GetMessagesAsync(Device, afterSequence);

    /// <summary>Last known positions this device may see (e.g. after an app restart).</summary>
    public Task<IReadOnlyList<SequencedMessage>> LatestLocations() => lobbies.GetLatestLocationsAsync(Device);

    private async Task MoveConnectionsAsync(Guid sessionId, IReadOnlyList<RoleMove> moves)
    {
        foreach (var move in moves.Where(m => m.OldRoleId != m.NewRoleId))
        {
            foreach (var connectionId in presence.ConnectionsOf(move.DeviceId))
            {
                await Groups.RemoveFromGroupAsync(connectionId, RoleGroup(sessionId, move.OldRoleId));
                await Groups.AddToGroupAsync(connectionId, RoleGroup(sessionId, move.NewRoleId));
            }
        }
    }

    private Task<IReadOnlyList<SyncPeer>> PeersAsync(Guid sessionId) =>
        lobbies.GetPeersAsync(sessionId, presence.ConnectedDevices);

    private async Task BroadcastPeersAsync(Guid sessionId) =>
        await Clients.Group(SessionGroup(sessionId)).SendAsync(OnlineProtocol.PeersMethod, await PeersAsync(sessionId));
}
