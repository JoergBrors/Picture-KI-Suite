using PictureGeoExif.Core.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace PictureGeoExif.Metadata.Decoding;

/// <summary>Cross-platform decoder for JPEG, PNG, TIFF, BMP, GIF and WebP (ImageSharp). No HEIC/RAW support.</summary>
public sealed class ImageSharpDecoder : IImageDecoder
{
    public string Name => "ImageSharp " + typeof(Image).Assembly.GetName().Version?.ToString(3);

    public IReadOnlyCollection<ImageFormat> SupportedFormats { get; } =
        [ImageFormat.Jpeg, ImageFormat.Png, ImageFormat.Tiff, ImageFormat.Bmp, ImageFormat.Gif, ImageFormat.WebP];

    public Task<byte[]?> CreatePreviewJpegAsync(string path, int maxEdge, CancellationToken token = default) => Task.Run(() =>
    {
        if (!SupportedFormats.Contains(ImageFormatDetector.DetectFile(path))) return null;
        using var image = Image.Load(path);
        token.ThrowIfCancellationRequested();
        image.Mutate(x => x.AutoOrient());
        if (Math.Max(image.Width, image.Height) > maxEdge)
            image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(maxEdge, maxEdge), Mode = ResizeMode.Max, Sampler = KnownResamplers.Lanczos3 }));
        image.Mutate(x => x.BackgroundColor(Color.White));
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream, new JpegEncoder { Quality = 80, SkipMetadata = true });
        return (byte[]?)stream.ToArray();
    }, token);
}

/// <summary>Tries decoders in order (e.g. ImageSharp, then macOS ImageIO) and returns the first rendering.</summary>
public sealed class CompositeImageDecoder(IEnumerable<IImageDecoder> decoders) : IImageDecoder
{
    private readonly IReadOnlyList<IImageDecoder> decoders = decoders.ToList();

    public IReadOnlyList<IImageDecoder> Decoders => decoders;
    public string Name => string.Join(" + ", decoders.Select(d => d.Name));
    public IReadOnlyCollection<ImageFormat> SupportedFormats => decoders.SelectMany(d => d.SupportedFormats).Distinct().ToList();

    public async Task<byte[]?> CreatePreviewJpegAsync(string path, int maxEdge, CancellationToken token = default)
    {
        var format = ImageFormatDetector.DetectFile(path);
        foreach (var decoder in decoders.Where(d => d.CanDecode(format)).Concat(decoders.Where(d => !d.CanDecode(format))))
        {
            try
            {
                if (await decoder.CreatePreviewJpegAsync(path, maxEdge, token) is { Length: > 0 } bytes) return bytes;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* try the next decoder */ }
        }
        return null;
    }
}
