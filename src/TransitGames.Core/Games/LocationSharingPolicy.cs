namespace TransitGames.Core.Games;

/// <summary>Players of <see cref="ViewerRoleId"/> may see the location of players of <see cref="SubjectRoleId"/>.</summary>
public sealed record LocationSharingRule(string ViewerRoleId, string SubjectRoleId);

/// <summary>
/// Which role may see whose location. Everything not explicitly allowed is private: a role always sees itself,
/// and never sees another role unless a rule says so. Sharing is one-way by design
/// (Hide and Seek: hiders see seekers; Tag: chasers see runners — never the other way round).
/// </summary>
public sealed class LocationSharingPolicy
{
    private readonly HashSet<LocationSharingRule> _rules;

    public LocationSharingPolicy(IEnumerable<LocationSharingRule> rules)
    {
        _rules = [.. rules];
        if (_rules.Any(r => r.ViewerRoleId == r.SubjectRoleId))
        {
            throw new ArgumentException("A role always sees itself; self-rules are not allowed.", nameof(rules));
        }
    }

    /// <summary>Nobody sees anybody else.</summary>
    public static LocationSharingPolicy None { get; } = new([]);

    public IReadOnlyCollection<LocationSharingRule> Rules => _rules;

    public bool CanSee(string viewerRoleId, string subjectRoleId) =>
        viewerRoleId == subjectRoleId || _rules.Contains(new LocationSharingRule(viewerRoleId, subjectRoleId));

    /// <summary>Whether the location of <paramref name="subjectRoleId"/> is ever shown to another role.</summary>
    public bool IsShared(string subjectRoleId) => _rules.Any(r => r.SubjectRoleId == subjectRoleId);

    public IReadOnlyList<string> ViewersOf(string subjectRoleId) =>
        _rules.Where(r => r.SubjectRoleId == subjectRoleId).Select(r => r.ViewerRoleId).ToList();

    /// <summary>Throws when <paramref name="subjectRoleId"/> must not reveal its location to anyone.</summary>
    public void EnsureMayShare(string subjectRoleId)
    {
        if (!IsShared(subjectRoleId))
        {
            throw new LocationPrivacyException(subjectRoleId);
        }
    }
}

public sealed class LocationPrivacyException(string roleId)
    : InvalidOperationException($"The location of role '{roleId}' is private in this game and must not be shared.")
{
    public string RoleId { get; } = roleId;
}
