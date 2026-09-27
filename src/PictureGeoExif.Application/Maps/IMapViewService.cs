using PictureGeoExif.Core.Geo;

namespace PictureGeoExif.Application.Maps;

public sealed record MapMarker(int Id, GeoCoordinate Position, string Label);

public enum MapLineStyle { RouteTrunk, RouteBranch, RoadOnRoad, RoadOffRoad, RoadBranch }

public sealed record MapPolyline(IReadOnlyList<GeoCoordinate> Points, MapLineStyle Style, int RouteNumber = 0);

public enum MapLayer { Images, Routes, Road }

/// <summary>
/// What the app needs from a map, independent of how it is rendered (formerly WebView2 + Leaflet via ExecuteScript).
/// Implemented by the Avalonia map view model; a WebView-based implementation can be added later without touching callers.
/// </summary>
public interface IMapViewService
{
    void SetMarkers(IReadOnlyList<MapMarker> markers, bool fit);
    void SetSelected(int? markerId, GeoCoordinate? position);
    /// <summary>The coordinate picked by click or reference image (not yet applied to any image).</summary>
    void SetCurrent(GeoCoordinate? position);
    void SetRoutes(IReadOnlyList<MapPolyline> routes);
    void SetRoadSegments(IReadOnlyList<MapPolyline> segments);
    void SetLayerVisible(MapLayer layer, bool visible);
    void SetGrid(bool enabled, double meters);
    void CenterOn(GeoCoordinate position);

    event EventHandler<GeoCoordinate>? CoordinateSelected;
    event EventHandler<int>? MarkerSelected;
}
