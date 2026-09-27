namespace PictureGeoExif.Core.Photos;

/// <summary>
/// Read-only access to the platform photo library (Apple Photos via PhotoKit on macOS).
/// UI code talks only to this interface, never to PhotoKit. Phase 1 never modifies the library.
/// </summary>
public interface IPhotoLibraryService
{
    /// <summary>True if the platform offers a photo library integration at all.</summary>
    bool IsAvailable { get; }

    Task<PhotoLibraryAccessStatus> GetAuthorizationStatusAsync();

    /// <summary>Shows the system permission prompt if the status is NotDetermined; otherwise returns the current status. Only call on a user action.</summary>
    Task<PhotoLibraryAccessStatus> RequestAuthorizationAsync();

    Task<IReadOnlyList<PhotoAlbum>> GetAlbumsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PhotoAsset>> GetAssetsAsync(PhotoQuery query, CancellationToken cancellationToken = default);

    Task<int> CountAssetsAsync(PhotoQuery query, CancellationToken cancellationToken = default);

    Task<PhotoAssetDetails?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default);

    /// <summary>Small JPEG rendering for lists; may use a local low-resolution version and never forces an iCloud download of the original.</summary>
    Task<byte[]?> GetThumbnailAsync(string assetId, int maxPixelSize, CancellationToken cancellationToken = default);

    /// <summary>Opens the unmodified original. The data may first be downloaded from iCloud.</summary>
    Task<Stream?> OpenOriginalAsync(string assetId, CancellationToken cancellationToken = default);

    Task<PhotoExportResult> ExportOriginalAsync(string assetId, string destinationPath, CancellationToken cancellationToken = default) =>
        ExportOriginalAsync(assetId, destinationPath, null, cancellationToken);

    /// <summary>
    /// Writes the unmodified original to <paramref name="destinationPath"/> (atomically, never overwriting an existing file).
    /// Downloads from iCloud if needed and reports progress.
    /// </summary>
    Task<PhotoExportResult> ExportOriginalAsync(string assetId, string destinationPath, IProgress<PhotoTransferProgress>? progress, CancellationToken cancellationToken = default);
}

/// <summary>Used on platforms without a photo library; every call reports <see cref="PhotoLibraryAccessStatus.Unavailable"/>.</summary>
public sealed class UnavailablePhotoLibraryService : IPhotoLibraryService
{
    public bool IsAvailable => false;
    public Task<PhotoLibraryAccessStatus> GetAuthorizationStatusAsync() => Task.FromResult(PhotoLibraryAccessStatus.Unavailable);
    public Task<PhotoLibraryAccessStatus> RequestAuthorizationAsync() => Task.FromResult(PhotoLibraryAccessStatus.Unavailable);
    public Task<IReadOnlyList<PhotoAlbum>> GetAlbumsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PhotoAlbum>>([]);
    public Task<IReadOnlyList<PhotoAsset>> GetAssetsAsync(PhotoQuery query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PhotoAsset>>([]);
    public Task<int> CountAssetsAsync(PhotoQuery query, CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<PhotoAssetDetails?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default) => Task.FromResult<PhotoAssetDetails?>(null);
    public Task<byte[]?> GetThumbnailAsync(string assetId, int maxPixelSize, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);
    public Task<Stream?> OpenOriginalAsync(string assetId, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
    public Task<PhotoExportResult> ExportOriginalAsync(string assetId, string destinationPath, IProgress<PhotoTransferProgress>? progress, CancellationToken cancellationToken = default) =>
        Task.FromResult(PhotoExportResult.Fail(PhotoExportError.NotAuthorized, "Keine Fotomediathek auf dieser Plattform."));
}
