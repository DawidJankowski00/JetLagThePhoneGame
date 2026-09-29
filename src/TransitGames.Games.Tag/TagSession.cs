using TransitGames.Core.Sessions;

namespace TransitGames.Games.Tag;

public sealed class TagState
{
}

public sealed class TagSession : GameSessionBase<TagSettings, TagState>
{
    public TagSession(Guid id, TagSettings settings, string localRoleId)
        : base(id, TagModule.GameId, settings, localRoleId)
    {
    }

    public TagSession(GameSessionSnapshot snapshot, TagSettings settings)
        : base(snapshot, settings)
    {
    }
}
