using PictureGeoExif.Application.Maps;
using PictureGeoExif.Core.Geo;

namespace PictureGeoExif.Application.Tests;

public class MapProjectionTests
{
    [Theory]
    [InlineData(0, 0, 3)]
    [InlineData(52.52, 13.405, 15)]
    [InlineData(-33.87, 151.21, 10)]
    public void PixelRoundTrip(double lat, double lon, int zoom)
    {
        var (x, y) = MapProjection.ToPixel(lat, lon, zoom);
        var back = MapProjection.ToCoordinate(x, y, zoom);
        Assert.Equal(lat, back.Latitude, 6);
        Assert.Equal(lon, back.Longitude, 6);
    }

    [Fact]
    public void Origin_And_MetersPerPixel()
    {
        Assert.Equal((128.0, 128.0), MapProjection.ToPixel(0, 0, 0));
        Assert.InRange(MapProjection.MetersPerPixel(0, 0), 156_543, 156_544);
    }

    [Fact]
    public void FitZoom_ContainsPoints()
    {
        var points = new[] { new GeoCoordinate(52.50, 13.30), new GeoCoordinate(52.55, 13.45) };
        int zoom = MapProjection.FitZoom(points, 800, 600);
        var a = MapProjection.ToPixel(52.50, 13.30, zoom);
        var b = MapProjection.ToPixel(52.55, 13.45, zoom);
        Assert.True(Math.Abs(a.X - b.X) <= 640 && Math.Abs(a.Y - b.Y) <= 480);
        Assert.True(zoom >= 10);
    }

    [Fact]
    public void TileUri_FillsTemplate() =>
        Assert.Equal("https://tile.openstreetmap.org/5/16/10.png", MapProjection.TileUri("https://tile.openstreetmap.org/{z}/{x}/{y}.png", 5, 16, 10).AbsoluteUri);

    [Fact]
    public void RouteLayers_TrunkAndBranchLines()
    {
        var points = new[] { new RoutePoint(0, 0, 0), new RoutePoint(1, 0, 0.001), new RoutePoint(2, 0, 0.002), new RoutePoint(3, 0.0005, 0.001) };
        var routes = RouteBuilder.Build(points, 500, 10);
        var lines = RouteLayers.Routes(routes);
        Assert.Contains(lines, l => l.Style == MapLineStyle.RouteTrunk);
        Assert.Contains(lines, l => l.Style == MapLineStyle.RouteBranch && l.Points.Count >= 2);
        var settings = new AppSettings { FilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json") };
        var (segments, distances) = RouteLayers.Road(routes, new Dictionary<string, RouteRoadMatch>(), settings);
        Assert.Empty(segments);
        Assert.Empty(distances);
    }
}

public class AppPathsTests
{
    [Fact]
    public void CurrentPlatform_UsesConventionalFolders()
    {
        var paths = AppPaths.ForCurrentPlatform();
        Assert.EndsWith("settings.json", paths.SettingsFile);
        Assert.StartsWith(paths.CacheFolder, paths.PhotosOriginalCacheFolder);
        if (OperatingSystem.IsMacOS())
        {
            Assert.Contains("Library/Application Support/PictureGeoExif", paths.SettingsFolder, StringComparison.Ordinal);
            Assert.Contains("Library/Caches/PictureGeoExif", paths.CacheFolder, StringComparison.Ordinal);
            Assert.Contains("Library/Logs/PictureGeoExif", paths.LogFolder, StringComparison.Ordinal);
        }
        if (OperatingSystem.IsWindows()) Assert.EndsWith("PictureExifclone", paths.SettingsFolder); // legacy settings keep working
    }
}
