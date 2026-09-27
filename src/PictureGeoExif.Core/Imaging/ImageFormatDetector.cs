namespace PictureGeoExif.Core.Imaging;

/// <summary>
/// Detects image formats by file signature (magic bytes) with the file extension as tie-breaker.
/// The signature wins, because files from Photos or messengers regularly carry a wrong extension
/// (e.g. HEIC content in a ".jpg").
/// </summary>
public static class ImageFormatDetector
{
    /// <summary>Extensions offered in file pickers and accepted by drag &amp; drop.</summary>
    public static readonly IReadOnlyList<string> SupportedExtensions =
    [
        ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".gif", ".webp",
        ".heic", ".heif", ".hif", ".avif", ".dng",
        ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".orf", ".rw2", ".raf", ".pef", ".srw", ".x3f", ".3fr", ".iiq", ".erf", ".rwl"
    ];

    private static readonly HashSet<string> RawExtensions =
        [".cr2", ".cr3", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".orf", ".rw2", ".raf", ".pef", ".srw", ".x3f", ".3fr", ".iiq", ".erf", ".rwl"];

    public static bool IsSupportedExtension(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static ImageFormat FromExtension(string? pathOrExtension)
    {
        if (string.IsNullOrEmpty(pathOrExtension)) return ImageFormat.Unknown;
        string ext = (pathOrExtension.StartsWith('.') ? pathOrExtension : Path.GetExtension(pathOrExtension)).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" or ".jpe" => ImageFormat.Jpeg,
            ".png" => ImageFormat.Png,
            ".tif" or ".tiff" => ImageFormat.Tiff,
            ".bmp" or ".dib" => ImageFormat.Bmp,
            ".gif" => ImageFormat.Gif,
            ".webp" => ImageFormat.WebP,
            ".heic" or ".hif" => ImageFormat.Heic,
            ".heif" => ImageFormat.Heif,
            ".avif" => ImageFormat.Avif,
            ".dng" => ImageFormat.Dng,
            _ when RawExtensions.Contains(ext) => ImageFormat.Raw,
            _ => ImageFormat.Unknown
        };
    }

    /// <summary>Maps Apple Uniform Type Identifiers (PHAssetResource.uniformTypeIdentifier) to formats.</summary>
    public static ImageFormat FromUniformTypeIdentifier(string? uti) => uti?.ToLowerInvariant() switch
    {
        null or "" => ImageFormat.Unknown,
        "public.jpeg" => ImageFormat.Jpeg,
        "public.png" => ImageFormat.Png,
        "public.tiff" => ImageFormat.Tiff,
        "com.microsoft.bmp" => ImageFormat.Bmp,
        "com.compuserve.gif" => ImageFormat.Gif,
        "org.webmproject.webp" => ImageFormat.WebP,
        "public.heic" => ImageFormat.Heic,
        "public.heif" or "public.heics" => ImageFormat.Heif,
        "public.avif" => ImageFormat.Avif,
        "com.adobe.raw-image" => ImageFormat.Dng,
        var u when u.Contains("raw-image", StringComparison.Ordinal) || u.EndsWith(".raw", StringComparison.Ordinal) => ImageFormat.Raw,
        _ => ImageFormat.Unknown
    };

    /// <summary>Detects the format from the first bytes; falls back to the extension when the signature is ambiguous (TIFF vs. DNG/RAW).</summary>
    public static ImageFormat Detect(ReadOnlySpan<byte> header, string? fileName = null)
    {
        var byExtension = FromExtension(fileName);
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return ImageFormat.Jpeg;
        if (header.Length >= 8 && header[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return ImageFormat.Png;
        if (header.Length >= 2 && header[0] == 'B' && header[1] == 'M') return ImageFormat.Bmp;
        if (header.Length >= 6 && header[..3].SequenceEqual("GIF"u8)) return ImageFormat.Gif;
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8)) return ImageFormat.WebP;
        if (header.Length >= 12 && header.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            // ISO base media: brand decides (HEIC stills, HEIF, AVIF, Canon CR3).
            string brand = System.Text.Encoding.ASCII.GetString(header.Slice(8, 4));
            return brand switch
            {
                "heic" or "heix" or "heim" or "heis" or "hevc" or "hevx" => ImageFormat.Heic,
                "mif1" or "msf1" or "heif" => byExtension is ImageFormat.Heic or ImageFormat.Avif ? byExtension : ImageFormat.Heif,
                "avif" or "avis" => ImageFormat.Avif,
                "crx " => ImageFormat.Raw,
                _ => byExtension
            };
        }
        bool tiff = header.Length >= 4 &&
                    ((header[0] == 'I' && header[1] == 'I' && header[2] == 0x2A && header[3] == 0x00) ||
                     (header[0] == 'M' && header[1] == 'M' && header[2] == 0x00 && header[3] == 0x2A));
        // DNG and most RAW formats are TIFF containers; only the extension tells them apart cheaply.
        if (tiff) return byExtension is ImageFormat.Dng or ImageFormat.Raw ? byExtension : ImageFormat.Tiff;
        if (header.Length >= 4 && header[0] == 'I' && header[1] == 'I' && header[2] == 'R' && (header[3] == 'O' || header[3] == 'S')) return ImageFormat.Raw; // ORF
        if (header.Length >= 4 && header[0] == 'I' && header[1] == 'I' && header[2] == 'U' && header[3] == 0) return ImageFormat.Raw; // RW2
        if (header.Length >= 8 && header[..8].SequenceEqual("FUJIFILM"u8)) return ImageFormat.Raw; // RAF
        return byExtension;
    }

    public static ImageFormat Detect(Stream stream, string? fileName = null)
    {
        Span<byte> header = stackalloc byte[32];
        long start = stream.CanSeek ? stream.Position : 0;
        int read = 0, n;
        while (read < header.Length && (n = stream.Read(header[read..])) > 0) read += n;
        if (stream.CanSeek) stream.Position = start;
        return Detect(header[..read], fileName);
    }

    public static ImageFormat DetectFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Detect(stream, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return FromExtension(path); }
    }

    public static string DefaultExtension(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => ".jpg",
        ImageFormat.Png => ".png",
        ImageFormat.Tiff => ".tif",
        ImageFormat.Bmp => ".bmp",
        ImageFormat.Gif => ".gif",
        ImageFormat.WebP => ".webp",
        ImageFormat.Heic => ".heic",
        ImageFormat.Heif => ".heif",
        ImageFormat.Avif => ".avif",
        ImageFormat.Dng => ".dng",
        _ => ""
    };
}
