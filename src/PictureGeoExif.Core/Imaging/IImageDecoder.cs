namespace PictureGeoExif.Core.Imaging;

/// <summary>
/// Renders previews and thumbnails. Implementations: ImageSharp (all platforms) and ImageIO on macOS (HEIC, DNG, RAW).
/// Decoders only read pixels; metadata always comes from the shared metadata core.
/// </summary>
public interface IImageDecoder
{
    string Name { get; }
    /// <summary>Formats this decoder renders on the current machine.</summary>
    IReadOnlyCollection<ImageFormat> SupportedFormats { get; }
    bool CanDecode(ImageFormat format) => SupportedFormats.Contains(format);
    /// <summary>Orientation-corrected JPEG whose longer edge is at most <paramref name="maxEdge"/>; null if this decoder cannot render the file.</summary>
    Task<byte[]?> CreatePreviewJpegAsync(string path, int maxEdge, CancellationToken token = default);
}
