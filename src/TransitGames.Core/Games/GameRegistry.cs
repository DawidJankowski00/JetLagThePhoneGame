namespace TransitGames.Core.Games;

/// <summary>All game modules registered in the DI container. The start screen is built from this list.</summary>
public sealed class GameRegistry
{
    private readonly Dictionary<string, IGameModule> _modules;

    public GameRegistry(IEnumerable<IGameModule> modules)
    {
        _modules = new Dictionary<string, IGameModule>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            if (!_modules.TryAdd(module.Id, module))
            {
                throw new InvalidOperationException($"Game module id '{module.Id}' is registered more than once.");
            }

            var roleIds = module.Roles.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            var badRule = module.LocationSharing.Rules
                .FirstOrDefault(r => !roleIds.Contains(r.ViewerRoleId) || !roleIds.Contains(r.SubjectRoleId));
            if (badRule is not null)
            {
                throw new InvalidOperationException(
                    $"Game module '{module.Id}' has a location sharing rule for an unknown role: {badRule}.");
            }
        }

        Modules = _modules.Values.ToList();
    }

    public IReadOnlyList<IGameModule> Modules { get; }

    public IGameModule Get(string id) =>
        _modules.TryGetValue(id, out var module)
            ? module
            : throw new KeyNotFoundException($"Game module '{id}' is not registered.");

    public bool TryGet(string id, out IGameModule? module) => _modules.TryGetValue(id, out module);
}
