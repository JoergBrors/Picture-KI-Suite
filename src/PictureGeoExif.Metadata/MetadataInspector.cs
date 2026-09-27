using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Bmp;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Gif;
using MetadataExtractor.Formats.Heif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.WebP;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Xmp;
using PictureGeoExif.Core.Geo;
using PictureGeoExif.Core.Imaging;
using PictureGeoExif.Metadata.Ai;
using XmpCore;

namespace PictureGeoExif.Metadata;

public enum MetadataGroup { General, Exif, Gps, Xmp, Iptc, Raw }

public sealed record MetadataEntry(MetadataGroup Group, string Directory, string Name, string Value);

/// <summary>Everything the metadata panel shows for one file. Built from the file bytes only; the origin (Finder, Photos, drag &amp; drop) is irrelevant.</summary>
public sealed record MetadataReport(
    string FileName,
    ImageFormat Format,
    long FileSize,
    int? Width,
    int? Height,
    DateTime? CaptureTime,
    GeoCoordinate? Gps,
    double? Altitude,
    string? CameraMake,
    string? CameraModel,
    bool HasXmpSidecar,
    IReadOnlyList<MetadataEntry> Entries,
    string? Error = null)
{
    public IEnumerable<MetadataEntry> Group(MetadataGroup group) => Entries.Where(e => e.Group == group);
}

/// <summary>Reads EXIF, GPS, XMP (embedded and sidecar), IPTC and all raw directories with MetadataExtractor.</summary>
public static class MetadataInspector
{
    public static MetadataReport Inspect(string path, string? displayName = null)
    {
        string name = displayName ?? Path.GetFileName(path);
        var format = ImageFormatDetector.DetectFile(path);
        long size = 0;
        try { size = new FileInfo(path).Length; } catch (IOException) { }
        string sidecar = AiMetadataService.SidecarPath(path);
        bool hasSidecar = File.Exists(sidecar);

        IReadOnlyList<MetadataExtractor.Directory> directories;
        try { directories = ImageMetadataReader.ReadMetadata(path); }
        catch (Exception ex) when (ex is ImageProcessingException or IOException or UnauthorizedAccessException)
        {
            return new MetadataReport(name, format, size, null, null, null, null, null, null, null, hasSidecar, [],
                "Metadaten konnten nicht gelesen werden: " + ex.Message);
        }
        return Build(name, format, size, directories, hasSidecar ? sidecar : null);
    }

    public static MetadataReport Build(string name, ImageFormat format, long size, IReadOnlyList<MetadataExtractor.Directory> directories, string? sidecarPath)
    {
        var entries = new List<MetadataEntry>();
        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var sub = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();

        GeoCoordinate? gps = null;
        if (gpsDirectory != null && gpsDirectory.TryGetGeoLocation(out var location))
            gps = GeoCoordinate.TryCreate(location.Latitude, location.Longitude);
        double? altitude = gpsDirectory != null && gpsDirectory.TryGetRational(GpsDirectory.TagAltitude, out var alt)
            ? (gpsDirectory.TryGetInt32(GpsDirectory.TagAltitudeRef, out var altRef) && altRef == 1 ? -alt.ToDouble() : alt.ToDouble()) : null;

        DateTime? capture = null;
        if (sub != null && sub.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var taken)) capture = taken;
        else if (ifd0 != null && ifd0.TryGetDateTime(ExifDirectoryBase.TagDateTime, out var changed)) capture = changed;

        (int? width, int? height) = Dimensions(directories);
        string? make = Clean(ifd0?.GetDescription(ExifDirectoryBase.TagMake));
        string? model = Clean(ifd0?.GetDescription(ExifDirectoryBase.TagModel));

        // General: the facts a user looks for first.
        void General(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) entries.Add(new(MetadataGroup.General, "Allgemein", label, value)); }
        General("Datei", name);
        General("Format", ImageFormatCapabilities.DisplayName(format));
        General("Dateigröße", size > 0 ? FormatSize(size) : null);
        General("Abmessungen", width is { } w && height is { } h ? $"{w} × {h} Pixel" : null);
        General("Aufnahmezeit", capture?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        General("Kamera", string.Join(" ", new[] { make, model }.Where(s => s != null)));
        General("Objektiv", Clean(sub?.GetDescription(ExifDirectoryBase.TagLensModel)));
        General("GPS", gps?.ToString());
        General("XMP-Sidecar", sidecarPath != null ? "vorhanden" : null);

        foreach (var directory in directories)
        {
            var group = directory switch
            {
                GpsDirectory => MetadataGroup.Gps,
                ExifDirectoryBase => MetadataGroup.Exif,
                IptcDirectory => MetadataGroup.Iptc,
                XmpDirectory => MetadataGroup.Xmp,
                _ => MetadataGroup.Raw
            };
            if (directory is XmpDirectory xmp) AddXmp(entries, xmp.XmpMeta, "XMP (eingebettet)");
            foreach (var tag in directory.Tags)
            {
                string value = Clean(tag.Description) ?? "";
                if (value.Length > 500) value = value[..500] + " …";
                if (group != MetadataGroup.Xmp) entries.Add(new(group, directory.Name, tag.Name, value));
                // Raw keeps the complete technical listing of every directory, including the grouped ones.
                entries.Add(new(MetadataGroup.Raw, directory.Name, $"{tag.Name} (0x{tag.Type:X4})", value));
            }
        }

        if (sidecarPath != null)
        {
            try
            {
                using var stream = File.OpenRead(sidecarPath);
                AddXmp(entries, XmpMetaFactory.Parse(stream), "XMP-Sidecar");
            }
            catch (Exception ex) when (ex is XmpException or IOException) { entries.Add(new(MetadataGroup.Xmp, "XMP-Sidecar", "Fehler", ex.Message)); }
        }

        return new MetadataReport(name, format, size, width, height, capture, gps, altitude, make, model, sidecarPath != null, entries);
    }

    private static void AddXmp(List<MetadataEntry> entries, IXmpMeta? xmp, string source)
    {
        if (xmp == null) return;
        foreach (var property in xmp.Properties)
        {
            if (string.IsNullOrEmpty(property.Path) || string.IsNullOrEmpty(property.Value)) continue;
            entries.Add(new(MetadataGroup.Xmp, source, property.Path, property.Value.Length > 500 ? property.Value[..500] + " …" : property.Value));
        }
    }

    private static (int?, int?) Dimensions(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        // Container headers first (exact pixel size), EXIF values last (may describe a thumbnail or be missing).
        foreach (var directory in directories)
        {
            (int w, int h) tags = directory switch
            {
                JpegDirectory => (JpegDirectory.TagImageWidth, JpegDirectory.TagImageHeight),
                PngDirectory { Name: "PNG-IHDR" } => (PngDirectory.TagImageWidth, PngDirectory.TagImageHeight),
                HeicImagePropertiesDirectory => (HeicImagePropertiesDirectory.TagImageWidth, HeicImagePropertiesDirectory.TagImageHeight),
                BmpHeaderDirectory => (BmpHeaderDirectory.TagImageWidth, BmpHeaderDirectory.TagImageHeight),
                GifHeaderDirectory => (GifHeaderDirectory.TagImageWidth, GifHeaderDirectory.TagImageHeight),
                WebPDirectory => (WebPDirectory.TagImageWidth, WebPDirectory.TagImageHeight),
                _ => (-1, -1)
            };
            if (tags.w >= 0 && directory.TryGetInt32(tags.w, out int width) && directory.TryGetInt32(tags.h, out int height) && width > 0 && height > 0)
                return (width, height);
        }
        foreach (var directory in directories.OfType<ExifSubIfdDirectory>())
            if (directory.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out int w) && directory.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out int h) && w > 0 && h > 0)
                return (w, h);
        foreach (var directory in directories.OfType<ExifIfd0Directory>())
            if (directory.TryGetInt32(ExifDirectoryBase.TagImageWidth, out int w) && directory.TryGetInt32(ExifDirectoryBase.TagImageHeight, out int h) && w > 0 && h > 0)
                return (w, h);
        return (null, null);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('\0');

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d / 1024d:0.0} MB"),
        >= 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:0.0} KB"),
        _ => $"{bytes} B"
    };

    /// <summary>Capabilities of the metadata core for the diagnostics window.</summary>
    public static IReadOnlyList<(string Capability, string Formats)> Capabilities { get; } =
    [
        ("EXIF lesen", "JPEG, PNG, TIFF, WebP, HEIC/HEIF, AVIF, DNG, CR2/CR3, NEF, ARW, ORF, RW2 u. a. (MetadataExtractor " + PictureGeoExif.Core.AppInfo.LibraryVersion(typeof(ImageMetadataReader)) + ")"),
        ("GPS schreiben", "JPEG (verlustfrei, nur EXIF-Segment), PNG, TIFF"),
        ("XMP lesen", "eingebettet (alle von MetadataExtractor unterstützten Formate) und Sidecar <Bild>.xmp"),
        ("XMP schreiben", "Sidecar <Bild>.xmp (XmpCore " + PictureGeoExif.Core.AppInfo.LibraryVersion(typeof(XmpMetaFactory)) + ")"),
        ("IPTC lesen", "JPEG (APP13), TIFF"),
    ];
}
