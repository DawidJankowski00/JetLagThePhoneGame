using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TransitGames.Core.Games;
using TransitGames.Core.Sessions;
using TransitGames.Core.Timing;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;

namespace TransitGames.App.ViewModels;

public sealed record HistoryItem(string Title, string Subtitle, string Result, string Status, Guid SessionId)
{
    public bool HasResult => !string.IsNullOrEmpty(Result);
}

public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly IGameSessionStore _store;
    private readonly GameRegistry _registry;
    private readonly INavigationService _navigation;
    private readonly IClock _clock;
    private readonly ILogger<HistoryViewModel> _logger;

    public HistoryViewModel(
        IGameSessionStore store,
        GameRegistry registry,
        INavigationService navigation,
        IClock clock,
        ILogger<HistoryViewModel> logger)
    {
        _store = store;
        _registry = registry;
        _navigation = navigation;
        _clock = clock;
        _logger = logger;
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
                module is null ? string.Empty : Result(module, s),
                status,
                s.SessionId));
        }

        IsEmpty = Items.Count == 0;
    }

    [RelayCommand]
    private Task OpenAsync(HistoryItem item) =>
        _navigation.GoToAsync(Routes.HistoryDetails, new Dictionary<string, object> { [HistoryDetailsViewModel.SessionIdKey] = item.SessionId });

    /// <summary>The result headline; a game saved by an older version that cannot be read just has none.</summary>
    private string Result(IGameModule module, GameSessionSnapshot snapshot)
    {
        try
        {
            return module.DescribeResult(module.RestoreSession(snapshot), _clock.UtcNow).Headline;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Describing the result of {SessionId} failed", snapshot.SessionId);
            return string.Empty;
        }
    }
}
