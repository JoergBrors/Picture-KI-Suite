using PictureGeoExif.Core.Geo;
using PictureGeoExif.Core.Imaging;
using PictureGeoExif.Core.Photos;

namespace PictureGeoExif.Platform.Mac.PhotoKit;

/// <summary>Pure mapping between PhotoKit raw values and the platform-neutral domain model.</summary>
public static class PhotoKitMapping
{
    // PHAccessLevel
    public const long AccessLevelReadWrite = 2;
    // PHAssetCollectionType
    public const long CollectionTypeAlbum = 1, CollectionTypeSmartAlbum = 2;
    // PHAssetCollectionSubtype (selection)
    public const long SubtypeAlbumRegular = 2, SubtypeAlbumCloudShared = 101;
    public const long SubtypeSmartFavorites = 203, SubtypeSmartAllHidden = 205, SubtypeSmartUserLibrary = 209, SubtypeSmartRecentlyDeleted = 1000000201;
    /// <summary>NSNotFound as returned by estimatedAssetCount when unknown.</summary>
    public const long NotFound = long.MaxValue;

    /// <summary>PHAuthorizationStatus: 0 notDetermined, 1 restricted, 2 denied, 3 authorized, 4 limited. Unknown future values are treated as denied (fail safe).</summary>
    public static PhotoLibraryAccessStatus MapAuthorizationStatus(long raw) => raw switch
    {
        0 => PhotoLibraryAccessStatus.NotDetermined,
        1 => PhotoLibraryAccessStatus.Restricted,
        2 => PhotoLibraryAccessStatus.Denied,
        3 => PhotoLibraryAccessStatus.Authorized,
        4 => PhotoLibraryAccessStatus.Limited,
        _ => PhotoLibraryAccessStatus.Denied
    };

    /// <summary>PHAssetMediaType: 1 image, 2 video, 3 audio.</summary>
    public static PhotoMediaKind MapMediaType(long raw) => raw switch
    {
        1 => PhotoMediaKind.Image,
        2 => PhotoMediaKind.Video,
        3 => PhotoMediaKind.Audio,
        _ => PhotoMediaKind.Unknown
    };

    public static IReadOnlyList<long> MediaTypes(PhotoMediaKind kinds)
    {
        var result = new List<long>();
        if (kinds.HasFlag(PhotoMediaKind.Image)) result.Add(1);
        if (kinds.HasFlag(PhotoMediaKind.Video)) result.Add(2);
        if (kinds.HasFlag(PhotoMediaKind.Audio)) result.Add(3);
        return result.Count == 0 ? [1] : result;
    }

    /// <summary>PHAssetMediaSubtype photo flags; RAW is derived from the resources.</summary>
    public static PhotoSubtypes MapSubtypes(ulong raw, IEnumerable<PhotoKitResourceRecord> resources)
    {
        var result = PhotoSubtypes.None;
        if ((raw & 1) != 0) result |= PhotoSubtypes.Panorama;
        if ((raw & 2) != 0) result |= PhotoSubtypes.Hdr;
        if ((raw & 4) != 0) result |= PhotoSubtypes.Screenshot;
        if ((raw & 8) != 0) result |= PhotoSubtypes.LivePhoto;
        if ((raw & 16) != 0) result |= PhotoSubtypes.DepthEffect;
        if (resources.Any(r => ImageFormatDetector.FromUniformTypeIdentifier(r.UniformTypeIdentifier) is ImageFormat.Raw or ImageFormat.Dng)) result |= PhotoSubtypes.Raw;
        return result;
    }

    /// <summary>PHAssetResourceType.</summary>
    public static PhotoResourceKind MapResourceType(long raw) => raw switch
    {
        1 => PhotoResourceKind.Photo,
        2 or 6 or 12 => PhotoResourceKind.Video,
        3 => PhotoResourceKind.Audio,
        4 => PhotoResourceKind.AlternatePhoto,
        5 => PhotoResourceKind.FullSizePhoto,
        7 => PhotoResourceKind.AdjustmentData,
        8 => PhotoResourceKind.AdjustmentBasePhoto,
        9 or 10 or 11 => PhotoResourceKind.PairedVideo,
        _ => PhotoResourceKind.Other
    };

    public static PhotoAssetResourceInfo MapResource(PhotoKitResourceRecord record) =>
        new(MapResourceType(record.Type), record.OriginalFilename, record.UniformTypeIdentifier);

    /// <summary>The primary resource describes name and type of an asset in listings: the original photo if present.</summary>
    public static PhotoKitResourceRecord? PrimaryResource(IReadOnlyList<PhotoKitResourceRecord> resources) =>
        OriginalResource(resources) ?? resources.FirstOrDefault();

    /// <summary>Index-preserving variant of <see cref="PhotoResourceSelection.SelectOriginal"/>.</summary>
    public static PhotoKitResourceRecord? OriginalResource(IReadOnlyList<PhotoKitResourceRecord> resources)
    {
        var selected = PhotoResourceSelection.SelectOriginal(resources.Select(MapResource));
        if (selected == null) return null;
        return resources.First(r => MapResource(r) == selected);
    }

    public static PhotoAsset MapAsset(PhotoKitAssetRecord record)
    {
        var primary = PrimaryResource(record.Resources);
        return new PhotoAsset
        {
            Id = record.LocalIdentifier,
            MediaKind = MapMediaType(record.MediaType),
            Subtypes = MapSubtypes(record.MediaSubtypes, record.Resources),
            CreationDate = FromUnix(record.CreationDateUnix),
            ModificationDate = FromUnix(record.ModificationDateUnix),
            PixelWidth = (int)Math.Clamp(record.PixelWidth, 0, int.MaxValue),
            PixelHeight = (int)Math.Clamp(record.PixelHeight, 0, int.MaxValue),
            IsFavorite = record.Favorite,
            // PhotoKit reports (0,0) or out-of-range values for "no location" in some cases; only valid positions are kept.
            Location = record.Latitude is { } lat && record.Longitude is { } lon && !(lat == 0 && lon == 0) ? GeoCoordinate.TryCreate(lat, lon) : null,
            OriginalFileName = primary?.OriginalFilename,
            UniformTypeIdentifier = primary?.UniformTypeIdentifier
        };
    }

    public static DateTimeOffset? FromUnix(double? seconds) =>
        seconds is { } s && double.IsFinite(s) && Math.Abs(s) < 253_402_300_799 ? DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(s * 1000)) : null;

    /// <summary>Maps collections to albums. Library and favourites are offered as fixed entries, hidden/deleted are never listed.</summary>
    public static PhotoAlbum? MapAlbum(PhotoKitCollectionRecord record)
    {
        int? count = record.EstimatedCount is >= 0 and < int.MaxValue ? (int)record.EstimatedCount : null;
        string title = string.IsNullOrWhiteSpace(record.Title) ? "Ohne Titel" : record.Title!;
        if (record.CollectionType == CollectionTypeAlbum)
            return new PhotoAlbum(record.LocalIdentifier, title, record.Subtype == SubtypeAlbumCloudShared ? PhotoAlbumKind.SharedAlbum : PhotoAlbumKind.UserAlbum, count);
        if (record.CollectionType == CollectionTypeSmartAlbum)
            return record.Subtype is SubtypeSmartFavorites or SubtypeSmartUserLibrary or SubtypeSmartAllHidden or SubtypeSmartRecentlyDeleted
                ? null : new PhotoAlbum(record.LocalIdentifier, title, PhotoAlbumKind.SmartAlbum, count);
        return null;
    }

    public static IReadOnlyList<PhotoAlbum> BuildAlbumList(IEnumerable<PhotoKitCollectionRecord> collections)
    {
        var mapped = collections.Select(MapAlbum).OfType<PhotoAlbum>().ToList();
        var comparer = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("de-DE"), ignoreCase: true);
        return
        [
            new PhotoAlbum(PhotoAlbum.LibraryId, "Mediathek", PhotoAlbumKind.Library, null),
            new PhotoAlbum(PhotoAlbum.FavoritesId, "Favoriten", PhotoAlbumKind.Favorites, null),
            .. mapped.Where(a => a.Kind is PhotoAlbumKind.UserAlbum or PhotoAlbumKind.SharedAlbum).OrderBy(a => a.Title, comparer),
            .. mapped.Where(a => a.Kind == PhotoAlbumKind.SmartAlbum).OrderBy(a => a.Title, comparer)
        ];
    }

    public static PhotoKitAssetQuery MapQuery(PhotoQuery query) => new(
        query.AlbumId is PhotoAlbum.LibraryId or PhotoAlbum.FavoritesId ? null : query.AlbumId,
        query.AlbumId == PhotoAlbum.FavoritesId,
        MediaTypes(query.MediaKinds),
        query.NewestFirst);

    // PHPhotosErrorDomain codes (PHPhotosError).
    public const long ErrorUserCancelled = 3072, ErrorNetworkAccessRequired = 3164, ErrorNetworkError = 3169, ErrorIdentifierNotFound = 3201,
        ErrorInvalidResource = 3302, ErrorMissingResource = 3303, ErrorNotEnoughSpace = 3305, ErrorAccessRestricted = 3310, ErrorAccessUserDenied = 3311;

    /// <summary>Maps an NSError of a resource request to an export error. Network and iCloud problems become <see cref="PhotoExportError.CloudUnavailable"/>.</summary>
    public static PhotoExportError MapDataError(string? domain, long code) => (domain, code) switch
    {
        (_, ErrorUserCancelled) when domain == "PHPhotosErrorDomain" => PhotoExportError.Cancelled,
        ("NSCocoaErrorDomain", 3072) => PhotoExportError.Cancelled,
        ("PHPhotosErrorDomain", ErrorNetworkAccessRequired or ErrorNetworkError) => PhotoExportError.CloudUnavailable,
        ("PHPhotosErrorDomain", ErrorIdentifierNotFound) => PhotoExportError.AssetNotFound,
        ("PHPhotosErrorDomain", ErrorInvalidResource or ErrorMissingResource) => PhotoExportError.NoOriginalResource,
        ("PHPhotosErrorDomain", ErrorNotEnoughSpace) => PhotoExportError.IoError,
        ("PHPhotosErrorDomain", ErrorAccessRestricted or ErrorAccessUserDenied) => PhotoExportError.NotAuthorized,
        ("NSURLErrorDomain", _) or ("CloudPhotoLibraryErrorDomain", _) or ("CKErrorDomain", _) => PhotoExportError.CloudUnavailable,
        _ => PhotoExportError.Unknown
    };
}
