using System.Text.Json;
using TransitGames.Core.Events;
using TransitGames.Core.Geography;

namespace TransitGames.Tracking.Export;

/// <summary>
/// Writes an RFC 7946 FeatureCollection: one LineString (MultiLineString when there are several segments) per track
/// and one Point per located game event. Point times follow the common <c>coordinateProperties.times</c>
/// convention (togeojson, gpx.studio).
/// </summary>
public static class GeoJsonTrackWriter
{
    public const string KindTrack = "track";
    public const string KindEvent = "event";

    public static void Write(Stream output, IReadOnlyList<TrackWithPoints> tracks, IReadOnlyList<GameEvent> events)
    {
        using var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteString("type", "FeatureCollection");
        json.WriteStartArray("features");

        foreach (var track in tracks)
        {
            WriteTrack(json, track);
        }

        foreach (var gameEvent in events.Where(e => e.Location is not null).OrderBy(e => e.Timestamp))
        {
            WriteEvent(json, gameEvent);
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteTrack(Utf8JsonWriter json, TrackWithPoints track)
    {
        // A LineString needs at least two positions; single-fix segments carry no line.
        var segments = track.Points
            .GroupBy(p => p.Segment)
            .OrderBy(g => g.Key)
            .Select(g => g.ToList())
            .Where(s => s.Count >= 2)
            .ToList();
        if (segments.Count == 0)
        {
            return;
        }

        json.WriteStartObject();
        json.WriteString("type", "Feature");

        json.WriteStartObject("geometry");
        if (segments.Count == 1)
        {
            json.WriteString("type", "LineString");
            json.WritePropertyName("coordinates");
            WriteLine(json, segments[0]);
        }
        else
        {
            json.WriteString("type", "MultiLineString");
            json.WriteStartArray("coordinates");
            foreach (var segment in segments)
            {
                WriteLine(json, segment);
            }

            json.WriteEndArray();
        }

        json.WriteEndObject();

        var info = track.Track;
        json.WriteStartObject("properties");
        json.WriteString("kind", KindTrack);
        json.WriteString("name", info.Name);
        WriteOptional(json, "device", info.DeviceName);
        WriteOptional(json, "game", info.GameId);
        WriteOptional(json, "role", info.RoleId);
        json.WriteString("startTime", GpxWriter.Time(segments[0][0].Timestamp));
        json.WriteString("endTime", GpxWriter.Time(segments[^1][^1].Timestamp));
        json.WriteNumber("distanceMeters", Math.Round(info.DistanceMeters, 1));
        json.WriteNumber("segments", segments.Count);

        json.WriteStartObject("coordinateProperties");
        json.WriteStartArray("times");
        foreach (var segment in segments)
        {
            if (segments.Count > 1)
            {
                json.WriteStartArray();
            }

            foreach (var point in segment)
            {
                json.WriteStringValue(GpxWriter.Time(point.Timestamp));
            }

            if (segments.Count > 1)
            {
                json.WriteEndArray();
            }
        }

        json.WriteEndArray();
        json.WriteEndObject();

        json.WriteEndObject();
        json.WriteEndObject();
    }

    private static void WriteEvent(Utf8JsonWriter json, GameEvent gameEvent)
    {
        json.WriteStartObject();
        json.WriteString("type", "Feature");
        json.WriteStartObject("geometry");
        json.WriteString("type", "Point");
        json.WritePropertyName("coordinates");
        WritePosition(json, gameEvent.Location!.Value);
        json.WriteEndObject();

        json.WriteStartObject("properties");
        json.WriteString("kind", KindEvent);
        json.WriteString("type", gameEvent.Type);
        json.WriteString("name", gameEvent.Title);
        WriteOptional(json, "details", gameEvent.Details);
        WriteOptional(json, "role", gameEvent.ActorRoleId);
        json.WriteString("time", GpxWriter.Time(gameEvent.Timestamp));
        if (gameEvent.Data is { Count: > 0 } data)
        {
            json.WriteStartObject("data");
            foreach (var (key, value) in data)
            {
                json.WriteString(key, value);
            }

            json.WriteEndObject();
        }

        json.WriteEndObject();
        json.WriteEndObject();
    }

    private static void WriteLine(Utf8JsonWriter json, IEnumerable<TrackPoint> points)
    {
        json.WriteStartArray();
        foreach (var point in points)
        {
            WritePosition(json, point.Point);
        }

        json.WriteEndArray();
    }

    private static void WritePosition(Utf8JsonWriter json, GeoPoint point)
    {
        // RFC 7946: longitude first; 7 decimals ≈ 1 cm.
        json.WriteStartArray();
        json.WriteNumberValue(Math.Round(point.Longitude, 7));
        json.WriteNumberValue(Math.Round(point.Latitude, 7));
        json.WriteEndArray();
    }

    private static void WriteOptional(Utf8JsonWriter json, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            json.WriteString(name, value);
        }
    }
}
