using TransitGames.Core.Geography;

namespace TransitGames.Core.Stations;

[Flags]
public enum TransitModes
{
    None = 0,
    Rail = 1,
    Subway = 2,
    Tram = 4,
    Bus = 8,

    Default = Rail | Subway | Tram,
    All = Rail | Subway | Tram | Bus,
}

/// <summary>A transit stop usable as a hiding spot / target.</summary>
public sealed record Station
{
    /// <summary>Stable id: "osm:node/123" for OSM data, "custom:{hash}" for user-added/imported ones.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required GeoPoint Location { get; init; }

    public TransitModes Modes { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsCustom { get; init; }
}
