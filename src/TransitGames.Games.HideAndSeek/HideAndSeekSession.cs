using TransitGames.Core.Sessions;

namespace TransitGames.Games.HideAndSeek;

public sealed class HideAndSeekState
{
}

public sealed class HideAndSeekSession : GameSessionBase<HideAndSeekSettings, HideAndSeekState>
{
    public HideAndSeekSession(Guid id, HideAndSeekSettings settings, string localRoleId)
        : base(id, HideAndSeekModule.GameId, settings, localRoleId)
    {
    }

    public HideAndSeekSession(GameSessionSnapshot snapshot, HideAndSeekSettings settings)
        : base(snapshot, settings)
    {
    }
}
