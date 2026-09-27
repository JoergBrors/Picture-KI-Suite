using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace PictureGeoExif.Application.Tests;

internal static class TestImages
{
    public static string Jpeg(string folder, string name = "a.jpg", int width = 64, int height = 48)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        using var image = new Image<Rgba32>(width, height, new Rgba32(10, 120, 200));
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.DateTimeOriginal, "2024:01:15 10:20:30");
        image.SaveAsJpeg(path, new JpegEncoder { Quality = 90 });
        return path;
    }
}
