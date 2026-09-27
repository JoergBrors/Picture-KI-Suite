using PictureGeoExif.Core.Geo;

namespace PictureGeoExif.Application.Maps;

/// <summary>Web Mercator (EPSG:3857) math for 256-px slippy-map tiles as used by OpenStreetMap.</summary>
public static class MapProjection
{
    public const int TileSize = 256;
    public const int MinZoom = 1, MaxZoom = 19;
    public const double MaxLatitude = 85.05112878;
    private const double EarthCircumference = 40_075_016.686;

    public static double WorldSize(double zoom) => TileSize * Math.Pow(2, zoom);

    /// <summary>World pixel coordinates at <paramref name="zoom"/> (0,0 = north-west corner).</summary>
    public static (double X, double Y) ToPixel(double latitude, double longitude, double zoom)
    {
        double lat = Math.Clamp(latitude, -MaxLatitude, MaxLatitude) * Math.PI / 180;
        double size = WorldSize(zoom);
        double x = (longitude + 180) / 360 * size;
        double y = (1 - Math.Log(Math.Tan(lat) + 1 / Math.Cos(lat)) / Math.PI) / 2 * size;
        return (x, y);
    }

    public static GeoCoordinate ToCoordinate(double x, double y, double zoom)
    {
        double size = WorldSize(zoom);
        double lon = x / size * 360 - 180;
        double n = Math.PI - 2 * Math.PI * y / size;
        double lat = 180 / Math.PI * Math.Atan(Math.Sinh(n));
        lon = ((lon + 180) % 360 + 360) % 360 - 180;
        return new GeoCoordinate(Math.Clamp(lat, -MaxLatitude, MaxLatitude), lon);
    }

    public static double MetersPerPixel(double latitude, double zoom) =>
        EarthCircumference * Math.Cos(Math.Clamp(latitude, -MaxLatitude, MaxLatitude) * Math.PI / 180) / WorldSize(zoom);

    /// <summary>Largest integer zoom at which all points fit into the viewport (with margin).</summary>
    public static int FitZoom(IReadOnlyCollection<GeoCoordinate> points, double width, double height, int maxZoom = 17)
    {
        if (points.Count == 0 || width <= 0 || height <= 0) return 3;
        for (int zoom = maxZoom; zoom > MinZoom; zoom--)
        {
            var pixels = points.Select(p => ToPixel(p.Latitude, p.Longitude, zoom)).ToList();
            if (pixels.Max(p => p.X) - pixels.Min(p => p.X) <= width * 0.8 && pixels.Max(p => p.Y) - pixels.Min(p => p.Y) <= height * 0.8)
                return zoom;
        }
        return MinZoom;
    }

    public static GeoCoordinate Center(IReadOnlyCollection<GeoCoordinate> points) =>
        points.Count == 0 ? new GeoCoordinate(51.1657, 10.4515) // Germany
            : new GeoCoordinate((points.Min(p => p.Latitude) + points.Max(p => p.Latitude)) / 2, (points.Min(p => p.Longitude) + points.Max(p => p.Longitude)) / 2);

    /// <summary>Tile URL from a template with {z}/{x}/{y} (and optional {s} subdomain).</summary>
    public static Uri TileUri(string template, int zoom, int x, int y) =>
        new(template.Replace("{s}", "a").Replace("{z}", zoom.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{x}", x.ToString(System.Globalization.CultureInfo.InvariantCulture)).Replace("{y}", y.ToString(System.Globalization.CultureInfo.InvariantCulture)));
}
