using CommunityToolkit.Mvvm.ComponentModel;
using PictureGeoExif.Application;
using PictureGeoExif.Application.Maps;
using PictureGeoExif.Core.Geo;

namespace PictureGeoExif.Desktop.ViewModels;

/// <summary>State of the OpenStreetMap view; the <see cref="Controls.OsmMapControl"/> renders it. Implements the UI-neutral <see cref="IMapViewService"/>.</summary>
public sealed partial class MapViewModel(AppSettings settings, AppPaths paths) : ObservableObject, IMapViewService
{
    public string TileUrl => settings.TileUrl;
    public string Attribution => "© " + settings.TileAttribution;
    public Uri? AttributionUrl => Uri.TryCreate(settings.TileAttributionUrl, UriKind.Absolute, out var uri) ? uri : null;
    public string TileCacheFolder => paths.MapTileCacheFolder;

    [ObservableProperty] private IReadOnlyList<MapMarker> markers = [];
    [ObservableProperty] private int? selectedMarkerId;
    [ObservableProperty] private GeoCoordinate? selectedPosition;
    [ObservableProperty] private GeoCoordinate? currentPosition;
    [ObservableProperty] private IReadOnlyList<MapPolyline> routes = [];
    [ObservableProperty] private IReadOnlyList<MapPolyline> roadSegments = [];
    [ObservableProperty] private bool showImages = true;
    [ObservableProperty] private bool showRoutes = true;
    [ObservableProperty] private bool showRoad = true;
    [ObservableProperty] private bool gridEnabled = settings.MapGridEnabled;
    [ObservableProperty] private double gridMeters = Math.Clamp(settings.MapGridMeters, 1, 1000);
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string info = "";

    partial void OnGridEnabledChanged(bool value) => settings.MapGridEnabled = value;
    partial void OnGridMetersChanged(double value) => settings.MapGridMeters = value;

    /// <summary>Raised when the view should zoom to all markers.</summary>
    public event EventHandler? FitRequested;
    public event EventHandler<GeoCoordinate>? CenterRequested;
    public event EventHandler<GeoCoordinate>? CoordinateSelected;
    public event EventHandler<int>? MarkerSelected;

    public void SetMarkers(IReadOnlyList<MapMarker> value, bool fit)
    {
        Markers = value;
        if (fit) FitRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SetSelected(int? markerId, GeoCoordinate? position)
    {
        SelectedMarkerId = markerId;
        SelectedPosition = position;
        if (position is { } p) CenterRequested?.Invoke(this, p);
    }

    public void SetCurrent(GeoCoordinate? position) => CurrentPosition = position;
    public void SetRoutes(IReadOnlyList<MapPolyline> value) => Routes = value;
    public void SetRoadSegments(IReadOnlyList<MapPolyline> value) => RoadSegments = value;

    public void SetLayerVisible(MapLayer layer, bool visible)
    {
        switch (layer)
        {
            case MapLayer.Images: ShowImages = visible; break;
            case MapLayer.Routes: ShowRoutes = visible; break;
            case MapLayer.Road: ShowRoad = visible; break;
        }
    }

    public void SetGrid(bool enabled, double meters) { GridEnabled = enabled; GridMeters = meters; }
    public void CenterOn(GeoCoordinate position) => CenterRequested?.Invoke(this, position);

    // Called by the control.
    public void OnClicked(GeoCoordinate position) => CoordinateSelected?.Invoke(this, position);
    public void OnMarkerClicked(int id) => MarkerSelected?.Invoke(this, id);
}
