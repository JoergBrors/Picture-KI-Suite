namespace PictureGeoExif.Core.Imaging;

public enum ImageFormat
{
    Unknown,
    Jpeg,
    Png,
    Tiff,
    Bmp,
    Gif,
    WebP,
    Heic,
    Heif,
    Avif,
    Dng,
    /// <summary>Camera RAW other than DNG (CR2, CR3, NEF, ARW, ORF, RW2, RAF, …).</summary>
    Raw
}

/// <summary>What the app can do with a format, independent of the platform decoder that renders thumbnails.</summary>
public static class ImageFormatCapabilities
{
    /// <summary>Formats whose GPS tags can be written losslessly by the metadata core (JPEG segment replace, PNG/TIFF re-encode).</summary>
    public static bool CanWriteGps(ImageFormat format) => format is ImageFormat.Jpeg or ImageFormat.Png or ImageFormat.Tiff;

    /// <summary>Formats the pixel editor can open (ImageSharp). HEIC/RAW need an export to PNG/JPEG first.</summary>
    public static bool CanEdit(ImageFormat format) => format is ImageFormat.Jpeg or ImageFormat.Png or ImageFormat.Tiff or ImageFormat.Bmp or ImageFormat.Gif or ImageFormat.WebP;

    /// <summary>Formats MetadataExtractor reads EXIF/XMP from.</summary>
    public static bool CanReadMetadata(ImageFormat format) => format != ImageFormat.Unknown;

    public static string DisplayName(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => "JPEG",
        ImageFormat.Png => "PNG",
        ImageFormat.Tiff => "TIFF",
        ImageFormat.Bmp => "BMP",
        ImageFormat.Gif => "GIF",
        ImageFormat.WebP => "WebP",
        ImageFormat.Heic => "HEIC",
        ImageFormat.Heif => "HEIF",
        ImageFormat.Avif => "AVIF",
        ImageFormat.Dng => "DNG",
        ImageFormat.Raw => "RAW",
        _ => "Unbekannt"
    };
}
