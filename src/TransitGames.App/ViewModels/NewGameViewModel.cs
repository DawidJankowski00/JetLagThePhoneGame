using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransitGames.App.Services;
using TransitGames.Core.Games;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;

namespace TransitGames.App.ViewModels;

/// <summary>New game wizard, step 1: role selection. Next: area (<see cref="AreaSetupViewModel"/>), stations.</summary>
public sealed partial class NewGameViewModel : ObservableObject, IQueryAttributable
{
    public const string GameIdKey = "gameId";

    private readonly GameRegistry _registry;
    private readonly NewGameWizard _wizard;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private IGameModule? _module;

    public NewGameViewModel(
        GameRegistry registry,
        NewGameWizard wizard,
        INavigationService navigation,
        IDialogService dialogs)
    {
        _registry = registry;
        _wizard = wizard;
        _navigation = navigation;
        _dialogs = dialogs;
    }

    public ObservableCollection<GameRole> Roles { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = UiStrings.NewGameTitle;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial GameRole? SelectedRole { get; set; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(GameIdKey, out var value) && value is string gameId)
        {
            _module = _registry.Get(gameId);
            _wizard.Begin(_module);
            Title = $"{_module.Icon} {_module.DisplayName}";
            Description = _module.Description;
            Roles.Clear();
            foreach (var role in _module.Roles)
            {
                Roles.Add(role);
            }

            SelectedRole = null;
        }
    }

    private bool CanGoNext() => SelectedRole is not null;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        if (_module is null || SelectedRole is null)
        {
            await _dialogs.AlertAsync(UiStrings.Error, UiStrings.SelectRoleFirst);
            return;
        }

        _wizard.Role = SelectedRole;
        await _navigation.GoToAsync(Routes.NewGameArea);
    }
}
