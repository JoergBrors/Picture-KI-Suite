using Avalonia;
using Avalonia.Headless;
using PictureGeoExif.Desktop;

[assembly: AvaloniaTestApplication(typeof(PictureGeoExif.Avalonia.Tests.TestAppBuilder))]

namespace PictureGeoExif.Avalonia.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
