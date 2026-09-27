using PictureGeoExif.Core.Imaging;
using PictureGeoExif.Platform.Mac;
using PictureGeoExif.Platform.Mac.PhotoKit;

namespace PictureGeoExif.Platform.Tests;

/// <summary>
/// Runs the real Objective-C/ImageIO interop on macOS (CI macOS job); returns immediately elsewhere.
/// Never requests Photos access (that would need a user) – it only loads the framework and reads the status.
/// </summary>
public class MacIntegrationTests
{
    [Fact]
    public void PhotoKit_Loads_AndStatusIsReadable()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var facade = new ObjCPhotoKitFacade();
        Assert.True(facade.IsAvailable);
        long status = facade.AuthorizationStatus();
        Assert.InRange(status, 0, 4);
    }

    [Fact]
    public async Task ImageIO_RendersJpegPreview()
    {
        if (!OperatingSystem.IsMacOS()) return;
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        using (var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(400, 100))
            SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, path);
        try
        {
            var jpeg = await new MacImageIODecoder().CreatePreviewJpegAsync(path, 100);
            Assert.NotNull(jpeg);
            Assert.Equal(ImageFormat.Jpeg, ImageFormatDetector.Detect(jpeg));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PlatformInfo_ReportsProductVersion()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var info = new MacPlatformInfo();
        Assert.Matches(@"^\d+(\.\d+)*$", info.OperatingSystemVersion);
        Assert.Equal("macOS", info.OperatingSystem);
    }

    /// <summary>Keychain round trip only when explicitly enabled (CI keychains may be locked).</summary>
    [Fact]
    public void Keychain_RoundTrip_WhenEnabled()
    {
        if (!OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("PGE_TEST_KEYCHAIN") != "1") return;
        var store = new MacKeychainCredentialStore();
        string target = "PictureGeoExif/Test-" + Guid.NewGuid().ToString("N");
        try
        {
            store.Write(target, "geheim");
            Assert.Equal("geheim", store.Read(target));
        }
        finally { store.Delete(target); }
        Assert.Null(store.Read(target));
    }
}
