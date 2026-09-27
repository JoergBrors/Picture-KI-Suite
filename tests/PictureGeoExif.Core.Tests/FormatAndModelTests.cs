using PictureGeoExif.Core.Geo;
using PictureGeoExif.Core.Imaging;
using PictureGeoExif.Core.IO;
using PictureGeoExif.Core.Photos;
using PictureGeoExif.Core.Sources;

namespace PictureGeoExif.Tests;

public class ImageFormatDetectorTests
{
    private static byte[] Ftyp(string brand) => [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', .. System.Text.Encoding.ASCII.GetBytes(brand), 0, 0, 0, 0];

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, null, ImageFormat.Jpeg)]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, null, ImageFormat.Png)]
    [InlineData(new byte[] { (byte)'I', (byte)'I', 0x2A, 0 }, "a.tif", ImageFormat.Tiff)]
    [InlineData(new byte[] { (byte)'M', (byte)'M', 0, 0x2A }, "a.DNG", ImageFormat.Dng)]
    [InlineData(new byte[] { (byte)'I', (byte)'I', 0x2A, 0 }, "a.nef", ImageFormat.Raw)]
    [InlineData(new byte[] { (byte)'B', (byte)'M', 0, 0 }, null, ImageFormat.Bmp)]
    [InlineData(new byte[] { (byte)'I', (byte)'I', (byte)'R', (byte)'O' }, "x.orf", ImageFormat.Raw)]
    [InlineData(new byte[] { 1, 2, 3 }, "x.jpeg", ImageFormat.Jpeg)]
    [InlineData(new byte[] { 1, 2, 3 }, "x.txt", ImageFormat.Unknown)]
    public void Signature_WinsOverExtension(byte[] header, string? name, ImageFormat expected) =>
        Assert.Equal(expected, ImageFormatDetector.Detect(header, name));

    [Theory]
    [InlineData("heic", "x.jpg", ImageFormat.Heic)] // HEIC content with a wrong extension
    [InlineData("mif1", "x.heic", ImageFormat.Heic)]
    [InlineData("mif1", "x.heif", ImageFormat.Heif)]
    [InlineData("avif", "x.avif", ImageFormat.Avif)]
    [InlineData("crx ", "x.cr3", ImageFormat.Raw)]
    public void IsoBaseMedia_BrandDecides(string brand, string name, ImageFormat expected) =>
        Assert.Equal(expected, ImageFormatDetector.Detect(Ftyp(brand), name));

    [Theory]
    [InlineData("public.heic", ImageFormat.Heic)]
    [InlineData("public.jpeg", ImageFormat.Jpeg)]
    [InlineData("com.adobe.raw-image", ImageFormat.Dng)]
    [InlineData("com.canon.cr3-raw-image", ImageFormat.Raw)]
    [InlineData("com.apple.quicktime-movie", ImageFormat.Unknown)]
    [InlineData(null, ImageFormat.Unknown)]
    public void UniformTypeIdentifiers(string? uti, ImageFormat expected) =>
        Assert.Equal(expected, ImageFormatDetector.FromUniformTypeIdentifier(uti));

    [Fact]
    public void Capabilities_GpsWriteOnlyForLosslessFormats()
    {
        Assert.True(ImageFormatCapabilities.CanWriteGps(ImageFormat.Jpeg));
        Assert.False(ImageFormatCapabilities.CanWriteGps(ImageFormat.Heic));
        Assert.False(ImageFormatCapabilities.CanWriteGps(ImageFormat.Bmp));
        Assert.False(ImageFormatCapabilities.CanEdit(ImageFormat.Raw));
    }

    [Fact]
    public void SupportedExtensions_IncludeHeicAndRaw()
    {
        Assert.True(ImageFormatDetector.IsSupportedExtension("/a/b/IMG_1.HEIC"));
        Assert.True(ImageFormatDetector.IsSupportedExtension("c.dng"));
        Assert.False(ImageFormatDetector.IsSupportedExtension("c.mov"));
    }
}

public sealed class AtomicFileTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "pge-atomic-" + Guid.NewGuid().ToString("N"));
    public AtomicFileTests() => Directory.CreateDirectory(folder);
    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public void Write_NoOverwrite_Throws_AndKeepsFile()
    {
        string path = Path.Combine(folder, "a.bin");
        AtomicFile.Write(path, [1]);
        Assert.Throws<IOException>(() => AtomicFile.Write(path, [2], overwrite: false));
        Assert.Equal([1], File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(folder)); // temporary file removed
    }

    [Fact]
    public async Task WriteAsync_StreamsAndReplaces()
    {
        string path = Path.Combine(folder, "b.bin");
        await AtomicFile.WriteAsync(path, new MemoryStream([1, 2, 3]));
        await AtomicFile.WriteAsync(path, new MemoryStream([4]));
        Assert.Equal([4], File.ReadAllBytes(path));
    }

    [Fact]
    public void FreePath_AddsCounter()
    {
        string path = Path.Combine(folder, "IMG.HEIC");
        Assert.Equal(path, AtomicFile.FreePath(path));
        File.WriteAllBytes(path, [0]);
        Assert.Equal(Path.Combine(folder, "IMG (2).HEIC"), AtomicFile.FreePath(path));
    }

    [Theory]
    [InlineData("IMG_0001.HEIC", "IMG_0001.HEIC")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("a:b*c?.jpg", "a_b_c_.jpg")]
    [InlineData(@"C:\Fotos\IMG_2.jpg", "IMG_2.jpg")]
    [InlineData("   ", "bild")]
    [InlineData(null, "bild")]
    public void SafeFileName_StripsPathsAndInvalidCharacters(string? input, string expected) =>
        Assert.Equal(expected, AtomicFile.SafeFileName(input));
}

public class GeoAndSourceModelTests
{
    [Fact]
    public void GeoCoordinate_ValidatesRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoCoordinate(91, 0));
        Assert.Null(GeoCoordinate.TryCreate(double.NaN, 1));
        Assert.Null(GeoCoordinate.TryCreate(1, null));
        Assert.Equal("52.520000, 13.405000", new GeoCoordinate(52.52, 13.405).ToString());
    }

    [Fact]
    public void PhotoAsset_BecomesReadOnlySourceWithoutLocalPath()
    {
        var item = ImageSourceItem.FromPhotoAsset(new PhotoAsset
        {
            Id = "ABC/L0/001", OriginalFileName = "IMG_1.HEIC", UniformTypeIdentifier = "public.heic",
            PixelWidth = 10, PixelHeight = 20, IsFavorite = true, Location = new GeoCoordinate(1, 2)
        });
        Assert.Equal(ImageSourceType.ApplePhotos, item.SourceType);
        Assert.Null(item.LocalPath);
        Assert.Equal("ABC/L0/001", item.PhotoKitAssetId);
        Assert.True(item.IsReadOnly);
        Assert.True(item.CanExport);
        Assert.False(item.CanEdit);
        Assert.Equal(ImageFormat.Heic, item.OriginalFormat);
    }

    [Fact]
    public void PhotoAsset_WithoutName_GetsExtensionFromType()
    {
        var item = ImageSourceItem.FromPhotoAsset(new PhotoAsset { Id = "x", UniformTypeIdentifier = "public.jpeg" });
        Assert.Equal("Foto.jpg", item.OriginalFileName);
    }

    [Fact]
    public void File_IsEditableWhenFormatAllows()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        try
        {
            var item = ImageSourceItem.FromFile(path);
            Assert.Equal(ImageSourceType.FileSystem, item.SourceType);
            Assert.Equal(path, item.LocalPath);
            Assert.True(item.CanEdit);
            Assert.Equal(ImageFormat.Png, item.OriginalFormat);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExportResult_HasUserFriendlyMessages()
    {
        Assert.Contains("iCloud", PhotoExportResult.Fail(PhotoExportError.CloudUnavailable, "x").UserMessage, StringComparison.Ordinal);
        Assert.True(PhotoExportResult.Ok("/a", "a", 1).Success);
    }

    [Fact]
    public async Task UnavailableLibrary_IsSafeToCall()
    {
        IPhotoLibraryService photos = new UnavailablePhotoLibraryService();
        Assert.False(photos.IsAvailable);
        Assert.Equal(PhotoLibraryAccessStatus.Unavailable, await photos.RequestAuthorizationAsync());
        Assert.Empty(await photos.GetAlbumsAsync());
        Assert.False((await photos.ExportOriginalAsync("x", "/tmp/x")).Success);
    }
}
