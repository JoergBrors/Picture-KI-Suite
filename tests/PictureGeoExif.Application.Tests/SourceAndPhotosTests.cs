using PictureGeoExif.Application.Photos;
using PictureGeoExif.Application.Sources;
using PictureGeoExif.Core.Imaging;
using PictureGeoExif.Core.Photos;
using PictureGeoExif.Core.Sources;
using PictureGeoExif.Metadata;

namespace PictureGeoExif.Application.Tests;

/// <summary>Minimal fake of the platform photo library for pipeline tests.</summary>
internal sealed class FakePhotoLibrary : IPhotoLibraryService
{
    public Dictionary<string, (PhotoAsset Asset, byte[] Original)> Assets { get; } = [];
    public int Exports { get; private set; }
    public bool IsAvailable => true;
    public Task<PhotoLibraryAccessStatus> GetAuthorizationStatusAsync() => Task.FromResult(PhotoLibraryAccessStatus.Authorized);
    public Task<PhotoLibraryAccessStatus> RequestAuthorizationAsync() => GetAuthorizationStatusAsync();
    public Task<IReadOnlyList<PhotoAlbum>> GetAlbumsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PhotoAlbum>>([]);
    public Task<IReadOnlyList<PhotoAsset>> GetAssetsAsync(PhotoQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PhotoAsset>>(Assets.Values.Select(a => a.Asset).Skip(query.Offset).Take(query.Limit).ToList());
    public Task<int> CountAssetsAsync(PhotoQuery query, CancellationToken cancellationToken = default) => Task.FromResult(Assets.Count);
    public Task<PhotoAssetDetails?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default) => Task.FromResult<PhotoAssetDetails?>(null);
    public Task<byte[]?> GetThumbnailAsync(string assetId, int maxPixelSize, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);
    public Task<Stream?> OpenOriginalAsync(string assetId, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(Assets.TryGetValue(assetId, out var a) ? new MemoryStream(a.Original) : null);

    public Task<PhotoExportResult> ExportOriginalAsync(string assetId, string destinationPath, IProgress<PhotoTransferProgress>? progress, CancellationToken cancellationToken = default)
    {
        Exports++;
        if (!Assets.TryGetValue(assetId, out var a)) return Task.FromResult(PhotoExportResult.Fail(PhotoExportError.CloudUnavailable, "offline"));
        if (File.Exists(destinationPath)) return Task.FromResult(PhotoExportResult.Fail(PhotoExportError.DestinationExists, "exists"));
        File.WriteAllBytes(destinationPath, a.Original);
        return Task.FromResult(PhotoExportResult.Ok(destinationPath, a.Asset.OriginalFileName, a.Original.Length));
    }
}

public sealed class SourceAndPhotosTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "pge-app-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

    [Fact]
    public async Task FileProvider_SkipsUnsupportedAndDuplicates()
    {
        string a = TestImages.Jpeg(folder, "a.jpg");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "x");
        var provider = new FileSystemImageSourceProvider();
        Assert.Single(provider.Add([a, a, Path.Combine(folder, "notes.txt"), Path.Combine(folder, "missing.jpg")]));
        Assert.Empty(provider.Add([a]));
        Assert.Equal(["a.jpg"], FileSystemImageSourceProvider.EnumerateFolder(folder).Select(Path.GetFileName));
        var items = await provider.GetImagesAsync();
        await using var stream = await provider.OpenImageAsync(items[0]);
        Assert.Equal(ImageFormat.Jpeg, ImageFormatDetector.Detect(stream));
        var replaced = provider.Replace(items[0], TestImages.Jpeg(folder, "b.jpg"));
        Assert.Equal("b.jpg", (await provider.GetImagesAsync()).Single().OriginalFileName);
        Assert.Equal(ImageSourceType.FileSystem, replaced.SourceType);
    }

    [Fact]
    public async Task PhotosProvider_PagesAndMapsAssets()
    {
        var library = new FakePhotoLibrary();
        for (int i = 0; i < 5; i++) library.Assets[$"id{i}"] = (new PhotoAsset { Id = $"id{i}", OriginalFileName = $"IMG_{i}.HEIC", UniformTypeIdentifier = "public.heic" }, [1]);
        var provider = new ApplePhotosImageSourceProvider(library, new PhotoQuery());
        Assert.Equal(5, await provider.CountAsync());
        var page = await provider.GetPageAsync(2, 2);
        Assert.Equal(["id2", "id3"], page.Select(p => p.PhotoKitAssetId));
        Assert.All(page, p => Assert.True(p.IsReadOnly));
        await using var original = await provider.OpenImageAsync(page[0]);
        Assert.Equal(1, original.ReadByte());
    }

    /// <summary>PHAsset → original → local file → the same metadata core as for files.</summary>
    [Fact]
    public async Task PhotoOriginal_IsCached_AndAnalysedByMetadataCore()
    {
        byte[] jpeg = File.ReadAllBytes(TestImages.Jpeg(Path.Combine(folder, "src")));
        var library = new FakePhotoLibrary();
        library.Assets["ABC/L0/001"] = (new PhotoAsset { Id = "ABC/L0/001", OriginalFileName = "IMG_1.JPG" }, jpeg);
        var cache = new PhotoOriginalCache(library, Path.Combine(folder, "cache"));

        var first = await cache.MaterializeAsync("ABC/L0/001", "IMG_1.JPG", null, CancellationToken.None);
        var second = await cache.MaterializeAsync("ABC/L0/001", "IMG_1.JPG", null, CancellationToken.None);
        Assert.True(first.Success);
        Assert.Equal(first.DestinationPath, second.DestinationPath);
        Assert.Equal(1, library.Exports); // second call served from cache
        Assert.DoesNotContain("ABC", first.DestinationPath!, StringComparison.Ordinal); // no PhotoKit id in paths

        var report = MetadataInspector.Inspect(first.DestinationPath!, "IMG_1.JPG");
        Assert.Equal(ImageFormat.Jpeg, report.Format);
        Assert.Equal(new DateTime(2024, 1, 15, 10, 20, 30), report.CaptureTime);

        Assert.True(cache.Clear() > 0);
        Assert.False(cache.TryGetCached("ABC/L0/001", "IMG_1.JPG", out _));
    }

    [Fact]
    public async Task PhotoOriginal_Failure_IsReported()
    {
        var cache = new PhotoOriginalCache(new FakePhotoLibrary(), Path.Combine(folder, "cache"));
        var result = await cache.MaterializeAsync("missing", "x.heic", null, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal(PhotoExportError.CloudUnavailable, result.Error);
    }
}
