namespace TransitGames.UI.Services;

/// <summary>Dialogs and navigation behind an interface so view models stay free of Shell statics.</summary>
public interface IDialogService
{
    Task AlertAsync(string title, string message, string? cancel = null);

    Task<bool> ConfirmAsync(string title, string message, string? accept = null, string? cancel = null);

    Task<string?> PromptAsync(string title, string message, string? initialValue = null, Keyboard? keyboard = null);

    Task<string?> ChooseAsync(string title, IReadOnlyList<string> options, string? cancel = null);
}

public interface INavigationService
{
    Task GoToAsync(string route, IDictionary<string, object>? parameters = null);

    Task GoBackAsync();

    /// <summary>Goes to the root start screen, clearing the navigation stack.</summary>
    Task GoHomeAsync();
}

internal sealed class ShellDialogService : IDialogService
{
    private static Page Page => Shell.Current ?? Application.Current!.Windows[0].Page!;

    public Task AlertAsync(string title, string message, string? cancel = null) =>
        Page.DisplayAlertAsync(title, message, cancel ?? Resources.UiStrings.Ok);

    public Task<bool> ConfirmAsync(string title, string message, string? accept = null, string? cancel = null) =>
        Page.DisplayAlertAsync(title, message, accept ?? Resources.UiStrings.Yes, cancel ?? Resources.UiStrings.No);

    public Task<string?> PromptAsync(string title, string message, string? initialValue = null, Keyboard? keyboard = null) =>
        Page.DisplayPromptAsync(
            title,
            message,
            Resources.UiStrings.Ok,
            Resources.UiStrings.Cancel,
            initialValue: initialValue ?? string.Empty,
            keyboard: keyboard ?? Keyboard.Default);

    public async Task<string?> ChooseAsync(string title, IReadOnlyList<string> options, string? cancel = null)
    {
        var cancelText = cancel ?? Resources.UiStrings.Cancel;
        var result = await Page.DisplayActionSheetAsync(title, cancelText, null, options.ToArray());
        return result == cancelText ? null : result;
    }
}

internal sealed class ShellNavigationService : INavigationService
{
    public const string HomeRoute = "//start";

    public Task GoToAsync(string route, IDictionary<string, object>? parameters = null) =>
        parameters is null ? Shell.Current.GoToAsync(route) : Shell.Current.GoToAsync(route, parameters);

    public Task GoBackAsync() => Shell.Current.GoToAsync("..");

    public Task GoHomeAsync() => Shell.Current.GoToAsync(HomeRoute);
}
