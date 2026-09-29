using CommunityToolkit.Mvvm.ComponentModel;
using TransitGames.Core.Sessions;
using TransitGames.UI.ViewModels;

namespace TransitGames.Games.HideAndSeek.Maui.ViewModels;

public sealed partial class HideAndSeekGameViewModel : ObservableObject
{
    private readonly GameSessionManager _sessions;

    public HideAndSeekGameViewModel(ActiveSessionViewModel header, GameSessionManager sessions)
    {
        Header = header;
        _sessions = sessions;
    }

    public ActiveSessionViewModel Header { get; }

    public HideAndSeekSession? Session => _sessions.Current as HideAndSeekSession;

    public bool IsSeeker => Session?.LocalRoleId == HideAndSeekRoles.Seeker;

    public bool IsHider => Session?.LocalRoleId == HideAndSeekRoles.Hider;

    public void OnAppearing()
    {
        Header.Activate();
        OnPropertyChanged(nameof(IsSeeker));
        OnPropertyChanged(nameof(IsHider));
    }

    public void OnDisappearing() => Header.Deactivate();
}
