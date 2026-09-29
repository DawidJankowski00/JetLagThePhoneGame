using TransitGames.Core.Sync;
using TransitGames.Sync;
using TransitGames.UI.Resources;

namespace TransitGames.UI.Services;

public static class OnlineErrors
{
    /// <summary>A user-facing message for a failed server call.</summary>
    public static string Describe(OnlineException error) => error.Code switch
    {
        OnlineErrorCodes.LobbyNotFound => UiStrings.OnlineErrorNotFound,
        OnlineErrorCodes.LobbyFinished => UiStrings.OnlineErrorFinished,
        OnlineException.IncompatibleServer => UiStrings.OnlineErrorIncompatible,
        OnlineException.UnknownGame => string.Format(UiStrings.OnlineErrorUnknownGame, error.Message),
        OnlineException.NetworkError => string.Format(UiStrings.OnlineErrorNetwork, error.Message),
        _ => error.Message,
    };

    public static Task ShowAsync(this IDialogService dialogs, OnlineException error) =>
        dialogs.AlertAsync(UiStrings.OnlineErrorTitle, Describe(error));
}
