using PictureGeoExif.Core.Geo;
using PictureGeoExif.Core.Imaging;

namespace PictureGeoExif.Core.Photos;

/// <summary>Platform-neutral photo library permission state (mirrors PHAuthorizationStatus plus "no library on this platform").</summary>
public enum PhotoLibraryAccessStatus
{
    /// <summary>No photo library integration on this platform (Windows, Linux) or the framework could not be loaded.</summary>
    Unavailable,
    NotDetermined,
    Restricted,
    Denied,
    Authorized,
    /// <summary>The user granted access to a selection of photos only (macOS 14+ limited library).</summary>
    Limited
}

public static class PhotoLibraryAccessStatusExtensions
{
    public static bool CanRead(this PhotoLibraryAccessStatus status) => status is PhotoLibraryAccessStatus.Authorized or PhotoLibraryAccessStatus.Limited;
    public static bool CanRequest(this PhotoLibraryAccessStatus status) => status == PhotoLibraryAccessStatus.NotDetermined;

    public static string DisplayText(this PhotoLibraryAccessStatus status) => status switch
    {
        PhotoLibraryAccessStatus.Unavailable => "Nicht verfügbar auf dieser Plattform",
        PhotoLibraryAccessStatus.NotDetermined => "Noch nicht autorisiert",
        PhotoLibraryAccessStatus.Restricted => "Eingeschränkt (z. B. durch Bildschirmzeit oder Verwaltung)",
        PhotoLibraryAccessStatus.Denied => "Abgelehnt – in den Systemeinstellungen unter Datenschutz & Sicherheit → Fotos änderbar",
        PhotoLibraryAccessStatus.Authorized => "Erlaubt",
        PhotoLibraryAccessStatus.Limited => "Eingeschränkt erlaubt (ausgewählte Fotos)",
        _ => status.ToString()
    };
}

[Flags]
public enum PhotoMediaKind
{
    None = 0,
    Image = 1,
    Video = 2,
    Audio = 4,
    Unknown = 8,
    All = Image | Video | Audio | Unknown
}

/// <summary>Photo subtypes (PHAssetMediaSubtype) relevant for filtering and display.</summary>
[Flags]
public enum PhotoSubtypes
{
    None = 0,
    Panorama = 1,
    Hdr = 2,
    Screenshot = 4,
    LivePhoto = 8,
    DepthEffect = 16,
    Raw = 32
}

public enum PhotoAlbumKind
{
    /// <summary>The whole library ("Alle Fotos").</summary>
    Library,
    Favorites,
    /// <summary>Album created by the user.</summary>
    UserAlbum,
    /// <summary>System-generated album (Selfies, Screenshots, Panoramas, RAW, …).</summary>
    SmartAlbum,
    SharedAlbum
}

/// <summary>An album or collection. <see cref="Id"/> is an opaque identifier of the platform (PhotoKit localIdentifier).</summary>
public sealed record PhotoAlbum(string Id, string Title, PhotoAlbumKind Kind, int? EstimatedCount)
{
    public const string LibraryId = "library:all";
    public const string FavoritesId = "library:favorites";
    public string DisplayTitle => EstimatedCount is { } n ? $"{Title} ({n})" : Title;
}

/// <summary>One asset in a listing. Loading it never downloads the original.</summary>
public sealed record PhotoAsset
{
    public required string Id { get; init; }
    public PhotoMediaKind MediaKind { get; init; } = PhotoMediaKind.Image;
    public PhotoSubtypes Subtypes { get; init; }
    public DateTimeOffset? CreationDate { get; init; }
    public DateTimeOffset? ModificationDate { get; init; }
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public bool IsFavorite { get; init; }
    public GeoCoordinate? Location { get; init; }
    /// <summary>Original file name of the primary resource, if known ("IMG_0001.HEIC").</summary>
    public string? OriginalFileName { get; init; }
    /// <summary>Uniform Type Identifier of the primary resource ("public.heic").</summary>
    public string? UniformTypeIdentifier { get; init; }
    public ImageFormat Format => PhotoResourceSelection.FormatOf(UniformTypeIdentifier, OriginalFileName);
}

public enum PhotoResourceKind
{
    Unknown,
    /// <summary>The original photo as imported (PHAssetResourceTypePhoto).</summary>
    Photo,
    /// <summary>Second original of a RAW+JPEG pair (PHAssetResourceTypeAlternatePhoto).</summary>
    AlternatePhoto,
    /// <summary>Current edited rendering (PHAssetResourceTypeFullSizePhoto).</summary>
    FullSizePhoto,
    AdjustmentData,
    AdjustmentBasePhoto,
    Video,
    PairedVideo,
    Audio,
    Other
}

public sealed record PhotoAssetResourceInfo(PhotoResourceKind Kind, string? OriginalFileName, string? UniformTypeIdentifier)
{
    public ImageFormat Format => PhotoResourceSelection.FormatOf(UniformTypeIdentifier, OriginalFileName);
}

/// <summary>Details of one asset including its resources. Still no original bytes.</summary>
public sealed record PhotoAssetDetails(PhotoAsset Asset, IReadOnlyList<PhotoAssetResourceInfo> Resources)
{
    /// <summary>The resource "Original exportieren" uses (see <see cref="PhotoResourceSelection"/>).</summary>
    public PhotoAssetResourceInfo? OriginalResource => PhotoResourceSelection.SelectOriginal(Resources);
    public bool HasEdits => Resources.Any(r => r.Kind is PhotoResourceKind.FullSizePhoto or PhotoResourceKind.AdjustmentData);
}

public static class PhotoResourceSelection
{
    /// <summary>The UTI is authoritative; the file name extension is the fallback.</summary>
    public static ImageFormat FormatOf(string? uniformTypeIdentifier, string? fileName) =>
        ImageFormatDetector.FromUniformTypeIdentifier(uniformTypeIdentifier) switch
        {
            ImageFormat.Unknown => ImageFormatDetector.FromExtension(fileName),
            var format => format
        };

    /// <summary>
    /// Picks the unedited original: Photo, then AlternatePhoto (RAW of a RAW+JPEG pair), then AdjustmentBasePhoto,
    /// and only if nothing else exists the edited FullSizePhoto.
    /// </summary>
    public static PhotoAssetResourceInfo? SelectOriginal(IEnumerable<PhotoAssetResourceInfo> resources)
    {
        var list = resources.ToList();
        foreach (var kind in new[] { PhotoResourceKind.Photo, PhotoResourceKind.AlternatePhoto, PhotoResourceKind.AdjustmentBasePhoto, PhotoResourceKind.FullSizePhoto })
            if (list.FirstOrDefault(r => r.Kind == kind) is { } found) return found;
        return null;
    }
}

public sealed record PhotoQuery
{
    /// <summary>Album id, <see cref="PhotoAlbum.LibraryId"/> or <see cref="PhotoAlbum.FavoritesId"/>.</summary>
    public string AlbumId { get; init; } = PhotoAlbum.LibraryId;
    public PhotoMediaKind MediaKinds { get; init; } = PhotoMediaKind.Image;
    public int Offset { get; init; }
    /// <summary>Page size; listings are always paged so large libraries never load at once.</summary>
    public int Limit { get; init; } = 120;
    public bool NewestFirst { get; init; } = true;
}

public enum PhotoExportError
{
    None,
    Cancelled,
    NotAuthorized,
    AssetNotFound,
    NoOriginalResource,
    /// <summary>The original is only in iCloud and could not be downloaded (offline, iCloud error, storage optimisation).</summary>
    CloudUnavailable,
    DestinationExists,
    IoError,
    Unknown
}

public sealed record PhotoExportResult(bool Success, string? DestinationPath, string? OriginalFileName, long BytesWritten,
    PhotoExportError Error = PhotoExportError.None, string? ErrorMessage = null)
{
    public static PhotoExportResult Ok(string path, string? name, long bytes) => new(true, path, name, bytes);
    public static PhotoExportResult Fail(PhotoExportError error, string message) => new(false, null, null, 0, error, message);

    /// <summary>Short, user-facing text; technical details belong in the log.</summary>
    public string UserMessage => Error switch
    {
        PhotoExportError.None => "Original exportiert.",
        PhotoExportError.Cancelled => "Vorgang abgebrochen.",
        PhotoExportError.NotAuthorized => "Kein Zugriff auf die Fotomediathek.",
        PhotoExportError.AssetNotFound => "Foto nicht mehr in der Mediathek vorhanden.",
        PhotoExportError.NoOriginalResource => "Für dieses Objekt ist kein Originalbild verfügbar.",
        PhotoExportError.CloudUnavailable => "Original derzeit nicht verfügbar (iCloud nicht erreichbar).",
        PhotoExportError.DestinationExists => "Zieldatei existiert bereits.",
        PhotoExportError.IoError => "Datei konnte nicht geschrieben werden.",
        _ => "Asset konnte nicht geladen werden."
    };
}

/// <summary>Progress of an original download/export. <see cref="Fraction"/> is 0…1 when the platform reports it.</summary>
public readonly record struct PhotoTransferProgress(double? Fraction, long BytesReceived, bool FromCloud);

public sealed class PhotoLibraryException(string message, Exception? inner = null) : Exception(message, inner);
