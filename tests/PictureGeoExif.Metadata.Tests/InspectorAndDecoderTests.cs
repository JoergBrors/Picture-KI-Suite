using PictureGeoExif.Core.Imaging;
using PictureGeoExif.Metadata;
using PictureGeoExif.Metadata.Decoding;
using PictureGeoExif.Metadata.Editing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace PictureGeoExif.Tests;

public sealed class InspectorAndDecoderTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "pge-inspect-" + Guid.NewGuid().ToString("N"));
    public InspectorAndDecoderTests() => System.IO.Directory.CreateDirectory(folder);
    public void Dispose() => System.IO.Directory.Delete(folder, true);

    private string Jpeg(int w = 120, int h = 80)
    {
        string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".jpg");
        using var image = new Image<Rgba32>(w, h, new Rgba32(200, 10, 10));
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Make, "TestCam");
        image.Metadata.ExifProfile.SetValue(ExifTag.DateTimeOriginal, "2023:07:01 08:00:00");
        image.SaveAsJpeg(path);
        return path;
    }

    [Fact]
    public void Inspect_GroupsGeneralExifGpsAndRaw()
    {
        string path = Jpeg();
        using (var service = new ImageService()) service.WriteGpsToImage(path, 47.5, 8.25);
        var report = MetadataInspector.Inspect(path);
        Assert.Equal(ImageFormat.Jpeg, report.Format);
        Assert.Equal((120, 80), (report.Width, report.Height));
        Assert.Equal(47.5, report.Gps!.Value.Latitude, 5);
        Assert.Equal("TestCam", report.CameraMake);
        Assert.Contains(report.Group(MetadataGroup.General), e => e.Name == "Kamera" && e.Value.Contains("TestCam", StringComparison.Ordinal));
        Assert.NotEmpty(report.Group(MetadataGroup.Exif));
        Assert.NotEmpty(report.Group(MetadataGroup.Gps));
        Assert.True(report.Group(MetadataGroup.Raw).Count() >= report.Group(MetadataGroup.Exif).Count());
        Assert.Null(report.Error);
    }

    [Fact]
    public void Inspect_ReportsSidecarXmp()
    {
        string path = Jpeg();
        File.WriteAllText(path + ".xmp", """
            <x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:subject><rdf:Bag><rdf:li>Berg</rdf:li></rdf:Bag></dc:subject></rdf:Description>
            </rdf:RDF></x:xmpmeta>
            """);
        var report = MetadataInspector.Inspect(path);
        Assert.True(report.HasXmpSidecar);
        Assert.Contains(report.Group(MetadataGroup.Xmp), e => e.Directory == "XMP-Sidecar" && e.Value == "Berg");
    }

    [Fact]
    public void Inspect_Garbage_GivesErrorInsteadOfException()
    {
        string path = Path.Combine(folder, "broken.jpg");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        var report = MetadataInspector.Inspect(path);
        Assert.NotNull(report.Error);
    }

    [Fact]
    public async Task Thumbnail_IsOrientedJpeg_AndCached()
    {
        string path = Jpeg(1000, 500);
        using var service = new ImageService();
        var first = await service.CreateThumbnailAsync(path, 200);
        var second = await service.CreateThumbnailAsync(path, 200);
        Assert.NotNull(first);
        Assert.Same(first, second);
        var info = Image.Identify(first);
        Assert.Equal((200, 100), (info.Width, info.Height));
        Assert.Null(info.Metadata.ExifProfile); // previews never carry metadata
    }

    [Fact]
    public async Task Heic_IsNotRenderedByImageSharp_ButCompositeFallsBack()
    {
        string heic = Path.Combine(folder, "x.heic");
        File.WriteAllBytes(heic, [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c', 0, 0, 0, 0]);
        Assert.Null(await new ImageSharpDecoder().CreatePreviewJpegAsync(heic, 100));
        var fallback = new CompositeImageDecoder([new ImageSharpDecoder(), new FixedDecoder()]);
        Assert.Equal([1, 2, 3], await fallback.CreatePreviewJpegAsync(heic, 100));
        Assert.Contains(ImageFormat.Heic, fallback.SupportedFormats);
    }

    [Fact]
    public void Heic_GpsWrite_IsRefused_FileUntouched()
    {
        string heic = Path.Combine(folder, "y.heic");
        byte[] bytes = [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c', 0, 0, 0, 0];
        File.WriteAllBytes(heic, bytes);
        using var service = new ImageService();
        var ex = Assert.Throws<InvalidOperationException>(() => service.WriteGpsToImage(heic, 1, 2));
        Assert.Contains("HEIC", ex.Message, StringComparison.Ordinal);
        Assert.Equal(bytes, File.ReadAllBytes(heic));
    }

    [Fact]
    public void StampFont_IsFoundOnEveryPlatform() => Assert.False(string.IsNullOrEmpty(EditorSession.StampFont().Name));

    private sealed class FixedDecoder : IImageDecoder
    {
        public string Name => "fixed";
        public IReadOnlyCollection<ImageFormat> SupportedFormats => [ImageFormat.Heic];
        public Task<byte[]?> CreatePreviewJpegAsync(string path, int maxEdge, CancellationToken token = default) => Task.FromResult<byte[]?>([1, 2, 3]);
    }
}
