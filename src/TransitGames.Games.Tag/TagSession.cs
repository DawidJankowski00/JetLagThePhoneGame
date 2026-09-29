using TransitGames.Core.Sessions;

namespace TransitGames.Games.Tag;

public sealed class TagState
{
}

public sealed class TagSession : GameSessionBase<TagSettings, TagState>
{
    public TagSession(TagSettings settings, string localRoleId)
        : base(TagModule.GameId, settings, localRoleId)
    {
    }

    public TagSession(GameSessionSnapshot snapshot, TagSettings settings)
        : base(snapshot, settings)
    {
    }
}
