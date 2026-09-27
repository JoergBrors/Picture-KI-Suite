using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PictureGeoExif.Core.Imaging;
using PictureGeoExif.Platform.Mac.Interop;

namespace PictureGeoExif.Platform.Mac;

/// <summary>
/// Previews through Apple ImageIO (system framework, no extra license): HEIC/HEIF, AVIF, DNG and camera RAW,
/// plus the common formats. Only renders pixels; metadata still comes from the shared metadata core.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacImageIODecoder : IImageDecoder
{
    [DllImport(CF.ImageIOLib)] private static extern IntPtr CGImageSourceCreateWithURL(IntPtr url, IntPtr options);
    [DllImport(CF.ImageIOLib)] private static extern IntPtr CGImageSourceCreateThumbnailAtIndex(IntPtr source, nint index, IntPtr options);
    [DllImport(CF.ImageIOLib)] private static extern IntPtr CGImageDestinationCreateWithData(IntPtr data, IntPtr type, nint count, IntPtr options);
    [DllImport(CF.ImageIOLib)] private static extern void CGImageDestinationAddImage(IntPtr destination, IntPtr image, IntPtr properties);
    [DllImport(CF.ImageIOLib)] private static extern byte CGImageDestinationFinalize(IntPtr destination);
    [DllImport(CF.CoreGraphicsLib)] private static extern void CGImageRelease(IntPtr image);

    public string Name => "macOS ImageIO";

    public IReadOnlyCollection<ImageFormat> SupportedFormats { get; } =
    [
        ImageFormat.Heic, ImageFormat.Heif, ImageFormat.Avif, ImageFormat.Dng, ImageFormat.Raw,
        ImageFormat.Jpeg, ImageFormat.Png, ImageFormat.Tiff, ImageFormat.Bmp, ImageFormat.Gif, ImageFormat.WebP
    ];

    public Task<byte[]?> CreatePreviewJpegAsync(string path, int maxEdge, CancellationToken token = default) =>
        Task.Run(() => Render(path, Math.Clamp(maxEdge, 16, 8192)), token);

    private static byte[]? Render(string path, int maxEdge)
    {
        IntPtr url = IntPtr.Zero, source = IntPtr.Zero, image = IntPtr.Zero, data = IntPtr.Zero, destination = IntPtr.Zero,
            options = IntPtr.Zero, size = IntPtr.Zero, jpegType = IntPtr.Zero, quality = IntPtr.Zero, properties = IntPtr.Zero;
        try
        {
            url = CF.FileUrl(path);
            source = CGImageSourceCreateWithURL(url, IntPtr.Zero);
            if (source == IntPtr.Zero) return null;
            size = CF.Int32(maxEdge);
            options = CF.Dictionary(
                (CF.Constant(CF.ImageIOLib, "kCGImageSourceCreateThumbnailFromImageAlways"), CF.True),
                (CF.Constant(CF.ImageIOLib, "kCGImageSourceCreateThumbnailWithTransform"), CF.True),
                (CF.Constant(CF.ImageIOLib, "kCGImageSourceThumbnailMaxPixelSize"), size));
            image = CGImageSourceCreateThumbnailAtIndex(source, 0, options);
            if (image == IntPtr.Zero) return null;

            data = CF.CFDataCreateMutable(IntPtr.Zero, 0);
            jpegType = CF.String("public.jpeg");
            destination = CGImageDestinationCreateWithData(data, jpegType, 1, IntPtr.Zero);
            if (destination == IntPtr.Zero) return null;
            quality = CF.Double(0.8);
            properties = CF.Dictionary((CF.Constant(CF.ImageIOLib, "kCGImageDestinationLossyCompressionQuality"), quality));
            CGImageDestinationAddImage(destination, image, properties);
            return CGImageDestinationFinalize(destination) != 0 ? CF.Bytes(data) : null;
        }
        finally
        {
            if (image != IntPtr.Zero) CGImageRelease(image);
            foreach (var handle in new[] { properties, quality, destination, jpegType, data, options, size, source, url }) CF.Release(handle);
        }
    }
}
