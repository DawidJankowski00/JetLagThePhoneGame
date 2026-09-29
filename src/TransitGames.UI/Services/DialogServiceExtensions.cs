namespace TransitGames.UI.Services;

public static class DialogServiceExtensions
{
    /// <summary>Action sheet over arbitrary items; returns the chosen item or default when cancelled.</summary>
    public static async Task<T?> ChooseAsync<T>(
        this IDialogService dialogs,
        string title,
        IReadOnlyList<T> items,
        Func<T, string> label)
    {
        // Numbered labels keep the mapping unambiguous even if two items render the same text.
        var labels = items.Select((item, i) => $"{i + 1}. {label(item)}").ToList();
        var chosen = await dialogs.ChooseAsync(title, labels);
        var index = chosen is null ? -1 : labels.IndexOf(chosen);
        return index >= 0 ? items[index] : default;
    }
}
