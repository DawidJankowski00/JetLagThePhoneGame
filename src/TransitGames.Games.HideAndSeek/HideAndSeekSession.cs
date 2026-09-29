using TransitGames.Core.Sessions;

namespace TransitGames.Games.HideAndSeek;

public sealed class HideAndSeekState
{
}

public sealed class HideAndSeekSession : GameSessionBase<HideAndSeekSettings, HideAndSeekState>
{
    public HideAndSeekSession(HideAndSeekSettings settings, string localRoleId)
        : base(HideAndSeekModule.GameId, settings, localRoleId)
    {
    }

    public HideAndSeekSession(GameSessionSnapshot snapshot, HideAndSeekSettings settings)
        : base(snapshot, settings)
    {
    }
}
