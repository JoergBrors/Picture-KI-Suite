using Microsoft.Extensions.DependencyInjection;
using PictureGeoExif.Application.Diagnostics;
using PictureGeoExif.Core.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PictureGeoExif.Desktop;

/// <summary>
/// "PictureGeoExif --self-test": starts the service container without UI, prints the diagnostics and renders a test image
/// through every decoder. Used by CI on macOS to prove that the bundle starts, native code loads and PhotoKit/ImageIO
/// are reachable. Never requests Photos access and never reads user data.
/// </summary>
internal static class SelfTest
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        int failures = 0;
        foreach (var item in await services.GetRequiredService<DiagnosticsService>().CollectAsync())
            Console.WriteLine($"{item.Name}: {item.Value}");

        string folder = Path.Combine(Path.GetTempPath(), "pge-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string png = Path.Combine(folder, "test.png");
            using (var image = new Image<Rgba32>(320, 200, new Rgba32(30, 144, 255))) image.SaveAsPng(png);
            var decoder = services.GetRequiredService<IImageDecoder>();
            var decoders = decoder is Metadata.Decoding.CompositeImageDecoder composite ? composite.Decoders : [decoder];
            foreach (var single in decoders)
            {
                byte[]? jpeg = null;
                try { jpeg = await single.CreatePreviewJpegAsync(png, 64); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { Console.WriteLine($"Decoder {single.Name}: FEHLER {ex.Message}"); }
                bool ok = jpeg is { Length: > 2 } && jpeg[0] == 0xFF && jpeg[1] == 0xD8;
                Console.WriteLine($"Decoder {single.Name}: {(ok ? "PASS" : "FAIL")}");
                if (!ok) failures++;
            }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }

        Console.WriteLine(failures == 0 ? "SELFTEST PASS" : $"SELFTEST FAIL ({failures})");
        return failures == 0 ? 0 : 1;
    }
}
