namespace TransitGames.Core.Sync;

/// <summary>
/// Reassigns the roles of a game, e.g. runners and chasers swap after a catch. It travels inside the game message
/// that causes it (<see cref="SyncMessage.RoleChange"/>), so every device applies it together with that message,
/// and the server moves the devices between role groups at the same moment (location privacy follows the new
/// roles). It applies only to the roles version it was made for: when two devices end the same round at once, the
/// roles change once.
/// </summary>
/// <param name="FromVersion">Roles version (number of changes so far) this change applies to.</param>
/// <param name="Mapping">Old role id → new role id; roles not listed stay.</param>
public sealed record RoleChange(int FromVersion, IReadOnlyDictionary<string, string> Mapping)
{
    public static RoleChange Swap(int fromVersion, string first, string second) =>
        new(fromVersion, new Dictionary<string, string> { [first] = second, [second] = first });

    public string Apply(string roleId) => Mapping.TryGetValue(roleId, out var next) ? next : roleId;

    /// <summary>True when the mapping only permutes <paramref name="roleIds"/> (no role disappears or appears).</summary>
    public bool IsPermutationOf(IEnumerable<string> roleIds)
    {
        var roles = roleIds.ToHashSet(StringComparer.Ordinal);
        return Mapping.Count > 0
            && Mapping.Keys.All(roles.Contains)
            && Mapping.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(Mapping.Values);
    }
}
