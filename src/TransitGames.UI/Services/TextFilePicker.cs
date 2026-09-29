namespace TransitGames.UI.Services;

public sealed record PickedTextFile(string FileName, string Content);

/// <summary>Lets the user pick a text file (GeoJSON import). Content is validated by the caller.</summary>
public interface ITextFilePicker
{
    /// <summary>Null when the user cancelled.</summary>
    Task<PickedTextFile?> PickAsync(string title);
}

public sealed class TextFilePicker : ITextFilePicker
{
    // .geojson has no registered MIME type/UTI on most devices, so accept any file and validate the content.
    private static readonly FilePickerFileType AnyFile = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.Android] = ["*/*"],
        [DevicePlatform.iOS] = ["public.json", "public.text", "public.data"],
    });

    private readonly IFilePicker _filePicker;

    public TextFilePicker(IFilePicker filePicker) => _filePicker = filePicker;

    public async Task<PickedTextFile?> PickAsync(string title)
    {
        var result = await _filePicker.PickAsync(new PickOptions { PickerTitle = title, FileTypes = AnyFile });
        if (result is null)
        {
            return null;
        }

        await using var stream = await result.OpenReadAsync();
        using var reader = new StreamReader(stream);
        return new PickedTextFile(result.FileName, await reader.ReadToEndAsync());
    }
}
