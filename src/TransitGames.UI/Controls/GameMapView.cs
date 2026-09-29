using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Mapsui;
using Mapsui.Layers;
using Mapsui.UI.Maui;
using Microsoft.Extensions.DependencyInjection;
using TransitGames.Core.Geography;
using TransitGames.Core.Stations;
using TransitGames.Geo.Areas;
using TransitGames.UI.Maps;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;
using Coordinate = NetTopologySuite.Geometries.Coordinate;
using GeometryFactories = TransitGames.Geo.Projection.GeometryFactories;
using Map = Mapsui.Map;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace TransitGames.UI.Controls;

/// <summary>
/// The map used on every screen: OSM tiles, game area, stations, a polygon being drawn and the own position.
/// Games add their own layers (zones, eliminated area, targets) with <see cref="AddLayer"/>.
/// </summary>
public sealed class GameMapView : ContentView
{
    public static readonly BindableProperty AreaProperty = BindableProperty.Create(
        nameof(Area), typeof(GameArea), typeof(GameMapView), propertyChanged: (b, _, _) => ((GameMapView)b).OnAreaChanged());

    public static readonly BindableProperty StationsProperty = BindableProperty.Create(
        nameof(Stations), typeof(IEnumerable<Station>), typeof(GameMapView),
        propertyChanged: (b, o, n) => ((GameMapView)b).OnCollectionChanged(o, n, v => v.RefreshStations()));

    public static readonly BindableProperty DraftPolygonProperty = BindableProperty.Create(
        nameof(DraftPolygon), typeof(IEnumerable<GeoPoint>), typeof(GameMapView),
        propertyChanged: (b, o, n) => ((GameMapView)b).OnCollectionChanged(o, n, v => v.RefreshDraft()));

    /// <summary>Polylines drawn under everything else, e.g. a recorded track (one list per segment).</summary>
    public static readonly BindableProperty TrackProperty = BindableProperty.Create(
        nameof(Track), typeof(IEnumerable<IReadOnlyList<GeoPoint>>), typeof(GameMapView), propertyChanged: (b, _, _) => ((GameMapView)b).OnTrackChanged());

    /// <summary>Event markers (questions asked, catches...).</summary>
    public static readonly BindableProperty WaypointsProperty = BindableProperty.Create(
        nameof(Waypoints), typeof(IEnumerable<GeoPoint>), typeof(GameMapView), propertyChanged: (b, _, _) => ((GameMapView)b).RefreshWaypoints());

    public static readonly BindableProperty OwnLocationProperty = BindableProperty.Create(
        nameof(OwnLocation), typeof(GeoPoint?), typeof(GameMapView), propertyChanged: (b, _, _) => ((GameMapView)b).RefreshOwnLocation());

    /// <summary>Executed with the tapped <see cref="GeoPoint"/> (when no station was hit).</summary>
    public static readonly BindableProperty TapCommandProperty = BindableProperty.Create(
        nameof(TapCommand), typeof(ICommand), typeof(GameMapView));

    /// <summary>Executed with the tapped <see cref="Station"/>.</summary>
    public static readonly BindableProperty StationTapCommandProperty = BindableProperty.Create(
        nameof(StationTapCommand), typeof(ICommand), typeof(GameMapView));

    private const double StationHitRadiusPixels = 28;

    // Warsaw; only used until an area or the own location is known.
    private static readonly GeoPoint DefaultCenter = new(52.2297, 21.0122);

    private readonly IMapLayerProvider _layers;
    private readonly MemoryLayer _areaLayer;
    private readonly MemoryLayer _trackLayer;
    private readonly MemoryLayer _waypointLayer;
    private readonly MemoryLayer _draftLayer;
    private readonly MemoryLayer _draftVertexLayer;
    private readonly MemoryLayer _stationsLayer;
    private readonly MemoryLayer _ownLocationLayer;

    public GameMapView()
    {
        var services = IPlatformApplication.Current?.Services
            ?? throw new InvalidOperationException("GameMapView needs the MAUI service provider.");
        _layers = services.GetRequiredService<IMapLayerProvider>();

        Map = new Map();
        Map.Layers.Add(_layers.CreateBaseLayer());
        Map.Layers.Add(_areaLayer = _layers.CreatePolygonLayer("area", MapPalette.PossibleArea, MapPalette.GameAreaOutline));
        Map.Layers.Add(_trackLayer = _layers.CreateLineLayer("track", MapPalette.Track));
        Map.Layers.Add(_waypointLayer = _layers.CreatePointLayer("waypoints"));
        Map.Layers.Add(_draftLayer = _layers.CreatePolygonLayer("draft", MapPalette.HidingZone, MapPalette.OwnPosition));
        Map.Layers.Add(_draftVertexLayer = _layers.CreatePointLayer("draft-vertices"));
        Map.Layers.Add(_stationsLayer = _layers.CreatePointLayer("stations"));
        Map.Layers.Add(_ownLocationLayer = _layers.CreatePointLayer("own-location"));
        Map.Tapped += OnMapTapped;
        Map.Navigator.CenterOnAndZoomTo(MapCoordinates.ToWorld(DefaultCenter), 20);

        var mapControl = new MapControl { Map = Map };
        var locate = new Button
        {
            Text = "◎",
            FontSize = 22,
            WidthRequest = 48,
            HeightRequest = 48,
            CornerRadius = 24,
            Padding = 0,
            Margin = new Thickness(12, 12, 12, 40), // keep the OSM attribution (drawn by Mapsui) visible
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End,
        };
        SemanticProperties.SetDescription(locate, UiStrings.MapCenterOnMe);
        locate.Clicked += async (_, _) => await CenterOnMeAsync(services.GetRequiredService<IDeviceLocationService>());

        Content = new Grid { Children = { mapControl, locate } };
    }

    public Map Map { get; }

    public GameArea? Area
    {
        get => (GameArea?)GetValue(AreaProperty);
        set => SetValue(AreaProperty, value);
    }

    public IEnumerable<Station>? Stations
    {
        get => (IEnumerable<Station>?)GetValue(StationsProperty);
        set => SetValue(StationsProperty, value);
    }

    public IEnumerable<GeoPoint>? DraftPolygon
    {
        get => (IEnumerable<GeoPoint>?)GetValue(DraftPolygonProperty);
        set => SetValue(DraftPolygonProperty, value);
    }

    public IEnumerable<IReadOnlyList<GeoPoint>>? Track
    {
        get => (IEnumerable<IReadOnlyList<GeoPoint>>?)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public IEnumerable<GeoPoint>? Waypoints
    {
        get => (IEnumerable<GeoPoint>?)GetValue(WaypointsProperty);
        set => SetValue(WaypointsProperty, value);
    }

    public GeoPoint? OwnLocation
    {
        get => (GeoPoint?)GetValue(OwnLocationProperty);
        set => SetValue(OwnLocationProperty, value);
    }

    public ICommand? TapCommand
    {
        get => (ICommand?)GetValue(TapCommandProperty);
        set => SetValue(TapCommandProperty, value);
    }

    public ICommand? StationTapCommand
    {
        get => (ICommand?)GetValue(StationTapCommandProperty);
        set => SetValue(StationTapCommandProperty, value);
    }

    /// <summary>Adds a game-specific layer below the stations and the own position.</summary>
    public void AddLayer(ILayer layer) => Map.Layers.Insert(Map.Layers.Count - 2, layer);

    /// <summary>Fits the game area on screen (Mapsui postpones this until the viewport has a size).</summary>
    public void ZoomToArea()
    {
        if (Area is not null)
        {
            Map.Navigator.ZoomToBox(MapCoordinates.ToWorld(Area.Geometry.EnvelopeInternal).Grow(250), MBoxFit.Fit);
        }
    }

    private void OnAreaChanged()
    {
        _layers.SetPolygon(_areaLayer, Area?.Geometry);
        ZoomToArea();
    }

    private void OnTrackChanged()
    {
        var lines = Track?.ToList() ?? [];
        _layers.SetLines(_trackLayer, lines);
        var points = lines.SelectMany(l => l).ToList();
        if (Area is null && points.Count > 0)
        {
            var envelope = new NetTopologySuite.Geometries.Envelope(
                points.Min(p => p.Longitude), points.Max(p => p.Longitude), points.Min(p => p.Latitude), points.Max(p => p.Latitude));
            Map.Navigator.ZoomToBox(MapCoordinates.ToWorld(envelope).Grow(150), MBoxFit.Fit);
        }
    }

    private void RefreshWaypoints() => _layers.SetPoints(_waypointLayer, Waypoints?.ToList() ?? [], MapPalette.Target, 0.5);

    private void RefreshStations() => _layers.SetStations(_stationsLayer, Stations?.ToList() ?? []);

    private void RefreshDraft()
    {
        var vertices = DraftPolygon?.ToList() ?? [];
        _layers.SetPoints(_draftVertexLayer, vertices, MapPalette.OwnPosition, 0.3);
        var geometry = vertices.Count switch
        {
            < 2 => null,
            2 => (NtsGeometry)GeometryFactories.Wgs84.CreateLineString(
                vertices.Select(v => new Coordinate(v.Longitude, v.Latitude)).ToArray()),
            _ => GeometryFactories.Wgs84.CreatePolygon(
                vertices.Append(vertices[0]).Select(v => new Coordinate(v.Longitude, v.Latitude)).ToArray()),
        };
        _layers.SetPolygon(_draftLayer, geometry);
    }

    private void RefreshOwnLocation() =>
        _layers.SetPoints(_ownLocationLayer, OwnLocation is { } p ? [p] : [], MapPalette.OwnPosition, 0.55);

    private void OnCollectionChanged(object? oldValue, object? newValue, Action<GameMapView> refresh)
    {
        if (oldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= OnAnyCollectionChanged;
        }

        if (newValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += OnAnyCollectionChanged;
        }

        refresh(this);
    }

    private void OnAnyCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, Stations))
        {
            RefreshStations();
        }

        if (ReferenceEquals(sender, DraftPolygon))
        {
            RefreshDraft();
        }
    }

    private void OnMapTapped(object? sender, MapEventArgs e)
    {
        var station = FindStationNear(e.WorldPosition);
        if (station is not null && StationTapCommand?.CanExecute(station) == true)
        {
            StationTapCommand.Execute(station);
            e.Handled = true;
            return;
        }

        var point = MapCoordinates.ToGeo(e.WorldPosition);
        if (TapCommand?.CanExecute(point) == true)
        {
            TapCommand.Execute(point);
            e.Handled = true;
        }
    }

    private Station? FindStationNear(MPoint world)
    {
        if (Stations is null)
        {
            return null;
        }

        var maxDistance = StationHitRadiusPixels * Map.Navigator.Viewport.Resolution;
        return Stations
            .Select(s => (Station: s, Distance: MapCoordinates.ToWorld(s.Location).Distance(world)))
            .Where(x => x.Distance <= maxDistance)
            .OrderBy(x => x.Distance)
            .Select(x => x.Station)
            .FirstOrDefault();
    }

    private async Task CenterOnMeAsync(IDeviceLocationService location)
    {
        var here = await location.TryGetCurrentAsync();
        if (here is null)
        {
            return;
        }

        OwnLocation = here;
        Map.Navigator.CenterOnAndZoomTo(MapCoordinates.ToWorld(here.Value), Math.Min(Map.Navigator.Viewport.Resolution, 5));
    }
}
