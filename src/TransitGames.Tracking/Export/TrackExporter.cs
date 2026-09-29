using System.Globalization;
using TransitGames.Core.Events;
using TransitGames.Core.Timing;
using TransitGames.Tracking.Storage;

namespace TransitGames.Tracking.Export;

public enum TrackFileFormat
{
    Gpx,
    GeoJson,
}

/// <summary>Exports a stored track together with the located events of its game session.</summary>
public sealed class TrackExporter
{
    private readonly ITrackStore _store;
    private readonly IGameEventLog _events;
    private readonly IClock _clock;

    public TrackExporter(ITrackStore store, IGameEventLog events, IClock clock)
    {
        _store = store;
        _events = events;
        _clock = clock;
    }

    public static string FileExtension(TrackFileFormat format) => format == TrackFileFormat.Gpx ? ".gpx" : ".geojson";

    public static string ContentType(TrackFileFormat format) =>
        format == TrackFileFormat.Gpx ? "application/gpx+xml" : "application/geo+json";

    /// <summary>E.g. "transitgames-2026-09-29-1530.gpx" (local start time).</summary>
    public static string FileName(TrackInfo track, TrackFileFormat format) =>
        "transitgames-"
        + track.StartedAt.ToLocalTime().ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture)
        + FileExtension(format);

    public async Task ExportAsync(Guid trackId, TrackFileFormat format, Stream output, CancellationToken cancellationToken = default)
    {
        var track = await _store.GetAsync(trackId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Track {trackId} does not exist.");
        var points = await _store.GetPointsAsync(trackId, cancellationToken).ConfigureAwait(false);
        var events = track.SessionId is { } sessionId
            ? await _events.GetEventsAsync(sessionId, cancellationToken).ConfigureAwait(false)
            : [];
        TrackWithPoints[] tracks = [new(track, points)];

        if (format == TrackFileFormat.Gpx)
        {
            GpxWriter.Write(output, track.Name, tracks, events, _clock.UtcNow);
        }
        else
        {
            GeoJsonTrackWriter.Write(output, tracks, events);
        }
    }

    /// <summary>Writes the export to <paramref name="directory"/> and returns the file path.</summary>
    public async Task<string> ExportToFileAsync(Guid trackId, TrackFileFormat format, string directory, CancellationToken cancellationToken = default)
    {
        var track = await _store.GetAsync(trackId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Track {trackId} does not exist.");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName(track, format));
        await using (var file = File.Create(path))
        {
            await ExportAsync(trackId, format, file, cancellationToken).ConfigureAwait(false);
        }

        return path;
    }
}
