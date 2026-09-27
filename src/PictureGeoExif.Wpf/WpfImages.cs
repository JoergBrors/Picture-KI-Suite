using System.IO;
using System.Windows.Media.Imaging;

namespace PictureExifclone;

/// <summary>Turns the toolkit-neutral JPEG thumbnails of the metadata core into frozen WPF bitmaps.</summary>
internal static class WpfImages
{
    public static BitmapImage? ToBitmap(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
