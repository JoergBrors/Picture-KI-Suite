using PictureGeoExif.Core.Photos;
using PictureGeoExif.Platform.Mac;
using PictureGeoExif.Platform.Mac.PhotoKit;

namespace PictureGeoExif.Platform.Tests;

public sealed class MacPhotoLibraryServiceTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "pge-photos-" + Guid.NewGuid().ToString("N"));
    private readonly FakePhotoKitFacade facade = new();
    private readonly IPhotoLibraryService service; // used through the interface, like the UI does

    public MacPhotoLibraryServiceTests()
    {
        Directory.CreateDirectory(folder);
        service = new MacPhotoLibraryService(facade);
    }

    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public async Task Unavailable_Framework_ReportsUnavailable()
    {
        facade.IsAvailable = false;
        Assert.False(service.IsAvailable);
        Assert.Equal(PhotoLibraryAccessStatus.Unavailable, await service.GetAuthorizationStatusAsync());
        Assert.False((await service.ExportOriginalAsync("x", Path.Combine(folder, "x.heic"))).Success);
    }

    [Fact]
    public async Task Request_OnlyPromptsWhenNotDetermined()
    {
        facade.Status = 2; // denied
        Assert.Equal(PhotoLibraryAccessStatus.Denied, await service.RequestAuthorizationAsync());
        Assert.Equal(0, facade.RequestCount);

        facade.Status = 0; facade.StatusAfterRequest = 4;
        Assert.Equal(PhotoLibraryAccessStatus.Limited, await service.RequestAuthorizationAsync());
        Assert.Equal(1, facade.RequestCount);
    }

    [Fact]
    public async Task Denied_ListsNothing_AndDoesNotThrow()
    {
        facade.Status = 2;
        facade.Assets.Add(FakePhotoKitFacade.Asset("a"));
        facade.Collections.Add(new("c", "Album", 1, 2, 1));
        Assert.Empty(await service.GetAlbumsAsync());
        Assert.Empty(await service.GetAssetsAsync(new PhotoQuery()));
        Assert.Equal(0, await service.CountAssetsAsync(new PhotoQuery()));
        var export = await service.ExportOriginalAsync("a", Path.Combine(folder, "a.heic"));
        Assert.Equal(PhotoExportError.NotAuthorized, export.Error);
    }

    [Fact]
    public async Task Paging_ReturnsImagesOnly_InPages()
    {
        for (int i = 0; i < 250; i++) facade.Assets.Add(FakePhotoKitFacade.Asset($"id{i}", mediaType: i % 10 == 0 ? 2 : 1, favorite: i % 3 == 0));
        Assert.Equal(225, await service.CountAssetsAsync(new PhotoQuery()));
        var first = await service.GetAssetsAsync(new PhotoQuery { Limit = 100 });
        var third = await service.GetAssetsAsync(new PhotoQuery { Offset = 200, Limit = 100 });
        Assert.Equal(100, first.Count);
        Assert.Equal(25, third.Count);
        Assert.All(first, a => Assert.Equal(PhotoMediaKind.Image, a.MediaKind));
        var favorites = await service.GetAssetsAsync(new PhotoQuery { AlbumId = PhotoAlbum.FavoritesId, Limit = 1000 });
        Assert.All(favorites, a => Assert.True(a.IsFavorite));
        Assert.True(facade.LastQuery!.FavoritesOnly);
    }

    [Fact]
    public async Task Details_ExposeResources_AndOriginal()
    {
        facade.Assets.Add(FakePhotoKitFacade.Asset("a", resources: [new(0, 1, "IMG_1.HEIC", "public.heic"), new(1, 5, "FullSizeRender.jpg", "public.jpeg")]));
        var details = await service.GetAssetAsync("a");
        Assert.NotNull(details);
        Assert.True(details.HasEdits);
        Assert.Equal("IMG_1.HEIC", details.OriginalResource!.OriginalFileName);
        Assert.Null(await service.GetAssetAsync("missing"));
    }

    [Fact]
    public async Task Export_WritesOriginalBytes_Atomically_AndNeverOverwrites()
    {
        byte[] heic = [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c', 1, 2, 3, 4, 5, 6];
        facade.Assets.Add(FakePhotoKitFacade.Asset("a", resources: [new(0, 5, "FullSizeRender.jpg", "public.jpeg"), new(1, 1, "IMG_1.HEIC", "public.heic")]));
        facade.Data[("a", 1)] = heic;
        string target = Path.Combine(folder, "IMG_1.HEIC");
        var progress = new List<double>();
        var result = await service.ExportOriginalAsync("a", target, new SyncProgress<PhotoTransferProgress>(p => { if (p.Fraction is { } f) progress.Add(f); }));
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(heic, File.ReadAllBytes(target));
        Assert.Equal("IMG_1.HEIC", result.OriginalFileName);
        Assert.Contains(1.0, progress);
        Assert.Single(Directory.GetFiles(folder)); // no temporary leftovers

        var again = await service.ExportOriginalAsync("a", target);
        Assert.Equal(PhotoExportError.DestinationExists, again.Error);
        Assert.Equal(heic, File.ReadAllBytes(target));
    }

    [Fact]
    public async Task Export_CloudFailure_LeavesNoFile_AndReportsReason()
    {
        facade.Assets.Add(FakePhotoKitFacade.Asset("a"));
        facade.FailWith = new PhotoKitDataResult(false, false, 0, "PHPhotosErrorDomain", PhotoKitMapping.ErrorNetworkAccessRequired, "offline");
        string target = Path.Combine(folder, "a.heic");
        var result = await service.ExportOriginalAsync("a", target);
        Assert.Equal(PhotoExportError.CloudUnavailable, result.Error);
        Assert.Contains("iCloud", result.UserMessage, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public async Task Export_Cancel_LeavesNoFile()
    {
        facade.Assets.Add(FakePhotoKitFacade.Asset("a"));
        facade.Data[("a", 0)] = [1, 2, 3];
        facade.Gate = new TaskCompletionSource();
        using var cts = new CancellationTokenSource();
        var task = service.ExportOriginalAsync("a", Path.Combine(folder, "a.heic"), cts.Token);
        cts.Cancel();
        var result = await task;
        Assert.Equal(PhotoExportError.Cancelled, result.Error);
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public async Task Export_UnknownAsset_OrNoOriginal()
    {
        Assert.Equal(PhotoExportError.AssetNotFound, (await service.ExportOriginalAsync("nope", Path.Combine(folder, "x"))).Error);
        facade.Assets.Add(FakePhotoKitFacade.Asset("video", resources: [new(0, 2, "v.mov", "com.apple.quicktime-movie")]));
        Assert.Equal(PhotoExportError.NoOriginalResource, (await service.ExportOriginalAsync("video", Path.Combine(folder, "v.mov"))).Error);
    }

    [Fact]
    public async Task OpenOriginal_ReturnsReadableStream_DeletedOnClose()
    {
        facade.Assets.Add(FakePhotoKitFacade.Asset("a"));
        facade.Data[("a", 0)] = [9, 8, 7];
        string path;
        await using (var stream = await service.OpenOriginalAsync("a"))
        {
            Assert.NotNull(stream);
            path = ((FileStream)stream).Name;
            var buffer = new byte[3];
            Assert.Equal(3, await stream.ReadAsync(buffer));
            Assert.Equal([9, 8, 7], buffer);
        }
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Thumbnail_UsesFacade()
    {
        facade.Assets.Add(FakePhotoKitFacade.Asset("a"));
        Assert.NotNull(await service.GetThumbnailAsync("a", 256));
        Assert.Null(await service.GetThumbnailAsync("missing", 256));
    }

    /// <summary>Progress&lt;T&gt; posts asynchronously; tests need synchronous reporting.</summary>
    private sealed class SyncProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
