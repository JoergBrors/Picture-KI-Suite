using PictureGeoExif.Platform.Windows;

namespace PictureGeoExif.Platform.Tests;

public class WindowsCredentialStoreTests
{
    [Fact]
    public void RoundTrip_OnWindows_OrUnsupportedElsewhere()
    {
        if (!OperatingSystem.IsWindows()) return; // Credential Manager exists only on Windows
        var store = new WindowsCredentialStore();
        string target = "PictureGeoExif/Test-" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.Null(store.Read(target));
            store.Write(target, "geheim");
            Assert.Equal("geheim", store.Read(target));
        }
        finally { store.Delete(target); }
        Assert.Null(store.Read(target));
    }
}
