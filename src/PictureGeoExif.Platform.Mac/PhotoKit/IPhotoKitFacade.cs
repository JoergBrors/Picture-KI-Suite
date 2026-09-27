namespace PictureGeoExif.Platform.Mac.PhotoKit;

/// <summary>
/// Raw, 1:1 view of the PhotoKit calls the adapter needs. Values are PhotoKit's own raw numbers; all interpretation
/// happens in <see cref="PhotoKitMapping"/> and <see cref="MacPhotoLibraryService"/>, so unit tests run with a fake facade
/// and without a real Photos library.
/// </summary>
public interface IPhotoKitFacade
{
    /// <summary>Photos.framework could be loaded.</summary>
    bool IsAvailable { get; }

    /// <summary>PHAuthorizationStatus for PHAccessLevelReadWrite.</summary>
    long AuthorizationStatus();

    /// <summary>PHPhotoLibrary requestAuthorizationForAccessLevel:handler: (read/write level is required to read the library).</summary>
    Task<long> RequestAuthorizationAsync();

    IReadOnlyList<PhotoKitCollectionRecord> FetchCollections();

    int CountAssets(PhotoKitAssetQuery query);

    IReadOnlyList<PhotoKitAssetRecord> FetchAssets(PhotoKitAssetQuery query, int offset, int limit);

    PhotoKitAssetRecord? FetchAsset(string localIdentifier);

    /// <summary>PHImageManager rendering as JPEG; never downloads from the network.</summary>
    byte[]? RequestThumbnailJpeg(string localIdentifier, int maxPixelSize);

    /// <summary>
    /// Streams the bytes of resource number <paramref name="resourceIndex"/> (index in assetResourcesForAsset:) into
    /// <paramref name="destination"/> via PHAssetResourceManager, allowing iCloud downloads. Cancelable.
    /// </summary>
    Task<PhotoKitDataResult> RequestResourceDataAsync(string localIdentifier, int resourceIndex, Stream destination,
        IProgress<double>? progress, CancellationToken token);
}

/// <summary>PHAssetCollection: assetCollectionType (1 album, 2 smart album), assetCollectionSubtype, estimatedAssetCount.</summary>
public sealed record PhotoKitCollectionRecord(string LocalIdentifier, string? Title, long CollectionType, long Subtype, long EstimatedCount);

/// <summary>PHAssetResource: type (PHAssetResourceType), originalFilename, uniformTypeIdentifier. Index = position in assetResourcesForAsset:.</summary>
public sealed record PhotoKitResourceRecord(int Index, long Type, string? OriginalFilename, string? UniformTypeIdentifier);

public sealed record PhotoKitAssetRecord(
    string LocalIdentifier,
    long MediaType,
    ulong MediaSubtypes,
    long PixelWidth,
    long PixelHeight,
    double? CreationDateUnix,
    double? ModificationDateUnix,
    bool Favorite,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<PhotoKitResourceRecord> Resources);

/// <summary>Collection id null = whole library. Media types are PHAssetMediaType values (1 image, 2 video, 3 audio).</summary>
public sealed record PhotoKitAssetQuery(string? CollectionId, bool FavoritesOnly, IReadOnlyList<long> MediaTypes, bool NewestFirst);

public sealed record PhotoKitDataResult(bool Success, bool Cancelled, long Bytes, string? ErrorDomain = null, long ErrorCode = 0, string? ErrorMessage = null);
