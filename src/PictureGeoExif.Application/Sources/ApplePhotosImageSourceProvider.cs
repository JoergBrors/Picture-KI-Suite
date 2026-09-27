using PictureGeoExif.Core.Photos;
using PictureGeoExif.Core.Sources;

namespace PictureGeoExif.Application.Sources;

/// <summary>Apple Photos album as image source. Paged; listing never downloads originals.</summary>
public sealed class ApplePhotosImageSourceProvider(IPhotoLibraryService photos, PhotoQuery query, string? name = null) : IPagedImageSourceProvider
{
    public const int DefaultPageSize = 120;

    public string Name { get; } = name ?? "Apple Fotos";
    public PhotoQuery Query => query;

    public Task<IReadOnlyList<ImageSourceItem>> GetImagesAsync(CancellationToken cancellationToken = default) =>
        GetPageAsync(0, DefaultPageSize, cancellationToken);

    public async Task<IReadOnlyList<ImageSourceItem>> GetPageAsync(int offset, int count, CancellationToken cancellationToken = default)
    {
        var assets = await photos.GetAssetsAsync(query with { Offset = Math.Max(0, offset), Limit = Math.Clamp(count, 1, 1000) }, cancellationToken);
        return assets.Select(ImageSourceItem.FromPhotoAsset).ToList();
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default) => photos.CountAssetsAsync(query, cancellationToken);

    public async Task<Stream> OpenImageAsync(ImageSourceItem image, CancellationToken cancellationToken = default)
    {
        if (image.PhotoKitAssetId is not { } id) throw new InvalidOperationException("Kein Apple-Fotos-Objekt.");
        return await photos.OpenOriginalAsync(id, cancellationToken) ?? throw new PhotoLibraryException("Original derzeit nicht verfügbar.");
    }
}
