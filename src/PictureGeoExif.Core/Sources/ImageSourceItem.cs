using PictureGeoExif.Core.Geo;
using PictureGeoExif.Core.Imaging;
using PictureGeoExif.Core.Photos;

namespace PictureGeoExif.Core.Sources;

public enum ImageSourceType
{
    /// <summary>A file on disk (dialog, Finder/Explorer drag &amp; drop, folder).</summary>
    FileSystem,
    /// <summary>An asset in Apple Photos (PhotoKit). Read-only in phase 1.</summary>
    ApplePhotos
}

/// <summary>
/// Identifies one image independent of where it comes from. A local path is optional, because PhotoKit assets
/// are not necessarily available as local files (iCloud). The core never needs to know the origin beyond these flags.
/// </summary>
public sealed record ImageSourceItem
{
    public required ImageSourceType SourceType { get; init; }
    /// <summary>Stable id within the source: full path for files, localIdentifier for PhotoKit.</summary>
    public required string SourceId { get; init; }
    public required string OriginalFileName { get; init; }
    public ImageFormat OriginalFormat { get; init; }
    public string? LocalPath { get; init; }
    public string? PhotoKitAssetId { get; init; }
    public bool IsReadOnly { get; init; }
    public bool CanExport { get; init; }
    public bool CanEdit { get; init; }
    public DateTimeOffset? CreationDate { get; init; }
    public int? PixelWidth { get; init; }
    public int? PixelHeight { get; init; }
    public bool IsFavorite { get; init; }
    /// <summary>Location known from the source listing (PhotoKit) before the original is read.</summary>
    public GeoCoordinate? Location { get; init; }

    public static ImageSourceItem FromFile(string path)
    {
        string full = Path.GetFullPath(path);
        var format = ImageFormatDetector.DetectFile(full);
        return new ImageSourceItem
        {
            SourceType = ImageSourceType.FileSystem,
            SourceId = full,
            OriginalFileName = Path.GetFileName(full),
            OriginalFormat = format,
            LocalPath = full,
            // Originals on disk are never modified; edits and GPS go to copies in the output folder.
            IsReadOnly = false,
            CanExport = true,
            CanEdit = ImageFormatCapabilities.CanEdit(format)
        };
    }

    public static ImageSourceItem FromPhotoAsset(PhotoAsset asset) => new()
    {
        SourceType = ImageSourceType.ApplePhotos,
        SourceId = asset.Id,
        PhotoKitAssetId = asset.Id,
        OriginalFileName = asset.OriginalFileName ?? "Foto" + ImageFormatDetector.DefaultExtension(asset.Format),
        OriginalFormat = asset.Format,
        IsReadOnly = true,
        CanExport = true,
        // Editing needs an exported copy first; the Photos original is never modified in phase 1.
        CanEdit = false,
        CreationDate = asset.CreationDate,
        PixelWidth = asset.PixelWidth,
        PixelHeight = asset.PixelHeight,
        IsFavorite = asset.IsFavorite,
        Location = asset.Location
    };
}
