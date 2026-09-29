using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TransitGames.Core.Games;
using TransitGames.Core.Sessions;
using TransitGames.UI.Resources;

namespace TransitGames.App.ViewModels;

public sealed record HistoryItem(string Title, string Subtitle, string Status, Guid SessionId);

public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly IGameSessionStore _store;
    private readonly GameRegistry _registry;

    public HistoryViewModel(IGameSessionStore store, GameRegistry registry)
    {
        _store = store;
        _registry = registry;
    }

    public ObservableCollection<HistoryItem> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    public async Task LoadAsync()
    {
        var snapshots = await _store.ListAsync();
        Items.Clear();
        foreach (var s in snapshots)
        {
            var module = _registry.TryGet(s.GameId, out var m) ? m : null;
            var role = module?.Roles.FirstOrDefault(r => r.Id == s.LocalRoleId)?.DisplayName ?? s.LocalRoleId;
            var status = s.Status switch
            {
                SessionStatus.Running => UiStrings.StatusRunning,
                SessionStatus.Finished => UiStrings.StatusFinished,
                _ => UiStrings.StatusSetup,
            };
            Items.Add(new HistoryItem(
                $"{module?.Icon} {module?.DisplayName ?? s.GameId}",
                $"{s.CreatedAt.ToLocalTime():g} · {role}",
                status,
                s.SessionId));
        }

        IsEmpty = Items.Count == 0;
    }
}
