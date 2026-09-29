using System.Globalization;
using System.Text;
using System.Xml;
using TransitGames.Core.Events;

namespace TransitGames.Tracking.Export;

/// <summary>A track with its points, as passed to the file writers.</summary>
public sealed record TrackWithPoints(TrackInfo Track, IReadOnlyList<TrackPoint> Points);

/// <summary>
/// Writes GPX 1.1 (valid against the official XSD): game events with a location become waypoints, each track
/// becomes a trk (one per player/device) and each recording segment a trkseg — split further where the device's
/// role changed (see <see cref="TrackRoles"/>), the role written as a <c>tg:role</c> extension of the trkseg.
/// </summary>
public static class GpxWriter
{
    public const string Namespace = "http://www.topografix.com/GPX/1/1";

    /// <summary>Namespace of our GPX extensions (<c>&lt;tg:role&gt;</c> on track segments).</summary>
    public const string ExtensionsNamespace = "urn:transitgames:gpx:1";
    private const string XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";
    private const string Creator = "Transit Games";

    public static void Write(
        Stream output,
        string name,
        IReadOnlyList<TrackWithPoints> tracks,
        IReadOnlyList<GameEvent> events,
        DateTimeOffset createdAt)
    {
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, CloseOutput = false };
        using var xml = XmlWriter.Create(output, settings);

        xml.WriteStartDocument();
        xml.WriteStartElement("gpx", Namespace);
        xml.WriteAttributeString("version", "1.1");
        xml.WriteAttributeString("creator", Creator);
        xml.WriteAttributeString("xmlns", "xsi", null, XsiNamespace);
        xml.WriteAttributeString("xmlns", "tg", null, ExtensionsNamespace);
        xml.WriteAttributeString("xsi", "schemaLocation", XsiNamespace, $"{Namespace} {Namespace}/gpx.xsd");

        WriteMetadata(xml, name, tracks, events, createdAt);

        foreach (var gameEvent in events.Where(e => e.Location is not null).OrderBy(e => e.Timestamp))
        {
            WriteWaypoint(xml, gameEvent);
        }

        foreach (var track in tracks)
        {
            WriteTrack(xml, track, TrackRoles.Split(track, events));
        }

        xml.WriteEndElement();
        xml.WriteEndDocument();
    }

    private static void WriteMetadata(
        XmlWriter xml, string name, IReadOnlyList<TrackWithPoints> tracks, IReadOnlyList<GameEvent> events, DateTimeOffset createdAt)
    {
        xml.WriteStartElement("metadata", Namespace);
        xml.WriteElementString("name", Namespace, name);
        xml.WriteElementString("time", Namespace, Time(createdAt));

        var points = tracks.SelectMany(t => t.Points).Select(p => p.Point)
            .Concat(events.Where(e => e.Location is not null).Select(e => e.Location!.Value))
            .ToList();
        if (points.Count > 0)
        {
            xml.WriteStartElement("bounds", Namespace);
            xml.WriteAttributeString("minlat", Coordinate(points.Min(p => p.Latitude)));
            xml.WriteAttributeString("minlon", Coordinate(points.Min(p => p.Longitude)));
            xml.WriteAttributeString("maxlat", Coordinate(points.Max(p => p.Latitude)));
            xml.WriteAttributeString("maxlon", Coordinate(points.Max(p => p.Longitude)));
            xml.WriteEndElement();
        }

        xml.WriteEndElement();
    }

    private static void WriteWaypoint(XmlWriter xml, GameEvent gameEvent)
    {
        var location = gameEvent.Location!.Value;
        xml.WriteStartElement("wpt", Namespace);
        xml.WriteAttributeString("lat", Coordinate(location.Latitude));
        xml.WriteAttributeString("lon", Coordinate(location.Longitude));
        xml.WriteElementString("time", Namespace, Time(gameEvent.Timestamp));
        xml.WriteElementString("name", Namespace, gameEvent.Title);
        if (!string.IsNullOrWhiteSpace(gameEvent.Details))
        {
            xml.WriteElementString("desc", Namespace, gameEvent.Details);
        }

        xml.WriteElementString("type", Namespace, gameEvent.Type);
        xml.WriteEndElement();
    }

    private static void WriteTrack(XmlWriter xml, TrackWithPoints track, IReadOnlyList<RolePiece> pieces)
    {
        xml.WriteStartElement("trk", Namespace);
        xml.WriteElementString("name", Namespace, track.Track.Name);
        if (!string.IsNullOrWhiteSpace(track.Track.DeviceName))
        {
            xml.WriteElementString("desc", Namespace, track.Track.DeviceName);
        }

        if (track.Track.GameId is { } gameId)
        {
            xml.WriteElementString("type", Namespace, gameId);
        }

        foreach (var piece in pieces)
        {
            xml.WriteStartElement("trkseg", Namespace);
            foreach (var point in piece.Points)
            {
                xml.WriteStartElement("trkpt", Namespace);
                xml.WriteAttributeString("lat", Coordinate(point.Point.Latitude));
                xml.WriteAttributeString("lon", Coordinate(point.Point.Longitude));
                if (point.AltitudeMeters is { } altitude)
                {
                    xml.WriteElementString("ele", Namespace, altitude.ToString("0.#", CultureInfo.InvariantCulture));
                }

                xml.WriteElementString("time", Namespace, Time(point.Timestamp));
                xml.WriteEndElement();
            }

            if (piece.Role is { } role)
            {
                xml.WriteStartElement("extensions", Namespace);
                xml.WriteElementString("role", ExtensionsNamespace, role);
                xml.WriteEndElement();
            }

            xml.WriteEndElement();
        }

        xml.WriteEndElement();
    }

    internal static string Coordinate(double degrees) => degrees.ToString("0.0######", CultureInfo.InvariantCulture);

    internal static string Time(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
