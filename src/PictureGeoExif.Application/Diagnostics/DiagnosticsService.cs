using System.Runtime.InteropServices;
using System.Text;
using PictureGeoExif.Core;
using PictureGeoExif.Core.Imaging;
using PictureGeoExif.Core.Photos;
using PictureGeoExif.Core.Platform;
using PictureGeoExif.Metadata;

namespace PictureGeoExif.Application.Diagnostics;

public sealed record DiagnosticsItem(string Name, string Value);

/// <summary>Collects technical facts for logging and Help → Diagnostics. No user data (no file names, GPS, asset ids).</summary>
public sealed class DiagnosticsService(IPlatformInfo platform, IPhotoLibraryService photos, IImageDecoder decoder, string uiFramework, AppPaths paths)
{
    public async Task<IReadOnlyList<DiagnosticsItem>> CollectAsync()
    {
        PhotoLibraryAccessStatus status;
        try { status = await photos.GetAuthorizationStatusAsync(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { status = PhotoLibraryAccessStatus.Unavailable; }

        var items = new List<DiagnosticsItem>
        {
            new("Application", AppInfo.ProductName),
            new("Application Version", AppInfo.Version),
            new(".NET Runtime", RuntimeInformation.FrameworkDescription),
            new("Runtime Identifier", RuntimeInformation.RuntimeIdentifier),
            new("Operating System", $"{platform.OperatingSystem} {platform.OperatingSystemVersion}"),
            new("OS Description", RuntimeInformation.OSDescription),
            new("Architecture", platform.ProcessArchitecture),
            new("ARM64", platform.IsArm64 ? "ja" : "nein"),
            new("UI Framework", uiFramework),
            new("App Bundle Path", platform.AppBundlePath ?? "(kein App-Bundle)"),
            new("Resources Path", platform.ResourcesPath),
            new("PhotoKit available", photos.IsAvailable ? "ja" : "nein"),
            new("PhotoKit authorization", status.ToString()),
            new("Image decoder", decoder.Name),
            new("Image decoder capabilities", string.Join(", ", decoder.SupportedFormats.Select(ImageFormatCapabilities.DisplayName))),
        };
        items.AddRange(MetadataInspector.Capabilities.Select(c => new DiagnosticsItem(c.Capability switch
        {
            "EXIF lesen" => "EXIF capability",
            "XMP lesen" => "XMP capability (read)",
            "XMP schreiben" => "XMP capability (write)",
            "GPS schreiben" => "GPS write capability",
            "IPTC lesen" => "IPTC capability",
            var other => other
        }, c.Formats)));
        items.Add(new("Log folder", paths.LogFolder));
        items.Add(new("Cache folder", paths.CacheFolder));
        return items;
    }

    public static string Format(IEnumerable<DiagnosticsItem> items)
    {
        var text = new StringBuilder();
        foreach (var item in items) text.Append(item.Name).Append(": ").AppendLine(item.Value);
        return text.ToString();
    }
}
