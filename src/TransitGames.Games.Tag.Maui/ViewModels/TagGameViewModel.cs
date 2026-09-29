using CommunityToolkit.Mvvm.ComponentModel;
using TransitGames.Core.Sessions;
using TransitGames.UI.ViewModels;

namespace TransitGames.Games.Tag.Maui.ViewModels;

public sealed partial class TagGameViewModel : ObservableObject
{
    private readonly GameSessionManager _sessions;

    public TagGameViewModel(ActiveSessionViewModel header, GameSessionManager sessions)
    {
        Header = header;
        _sessions = sessions;
    }

    public ActiveSessionViewModel Header { get; }

    public TagSession? Session => _sessions.Current as TagSession;

    public bool IsChaser => Session?.LocalRoleId == TagRoles.Chaser;

    public bool IsRunner => Session?.LocalRoleId == TagRoles.Runner;

    public void OnAppearing()
    {
        Header.Activate();
        OnPropertyChanged(nameof(IsChaser));
        OnPropertyChanged(nameof(IsRunner));
    }

    public void OnDisappearing() => Header.Deactivate();
}
