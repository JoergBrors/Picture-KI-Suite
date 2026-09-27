using System.Collections.Concurrent;
using MetadataExtractor.Formats.Exif;
using PictureGeoExif.Core.Geo;
using PictureGeoExif.Core.Imaging;
using PictureGeoExif.Core.IO;
using PictureGeoExif.Metadata.Decoding;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace PictureGeoExif.Metadata;

/// <summary>
/// Thumbnails, GPS reading/writing and collision-free exports. UI-toolkit independent: thumbnails are JPEG bytes
/// that WPF and Avalonia turn into their own bitmap types. Originals are never modified by the app workflows;
/// <see cref="WriteGpsToImage"/> is only called on copies.
/// </summary>
public sealed class ImageService : IDisposable
{
    private readonly IImageDecoder decoder;
    private readonly string tempFolder;
    private bool disposed;

    // Bounded thumbnail cache keyed by path, size and last write time, so a changed file never shows a stale thumbnail.
    private readonly ConcurrentDictionary<string, byte[]> thumbnailCache = new();
    private readonly ConcurrentQueue<string> cacheOrder = new();
    private const int MaxCachedThumbnails = 600;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> thumbnailLocks = new();

    public ImageService() : this(new ImageSharpDecoder()) { }

    public ImageService(IImageDecoder decoder)
    {
        this.decoder = decoder;
        tempFolder = Path.Combine(Path.GetTempPath(), $"PictureGeoExif_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempFolder);
    }

    public IImageDecoder Decoder => decoder;

    /// <summary>Orientation-corrected JPEG thumbnail (longer edge ≤ <paramref name="maxEdge"/>), cached; null if no decoder can render the file.</summary>
    public async Task<byte[]?> CreateThumbnailAsync(string imagePath, int maxEdge = 200, CancellationToken token = default)
    {
        if (disposed || string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return null;
        string key = CacheKey(imagePath, maxEdge);
        if (thumbnailCache.TryGetValue(key, out var cached)) return cached;

        var gate = thumbnailLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (thumbnailCache.TryGetValue(key, out cached)) return cached;
            byte[]? bytes;
            try { bytes = await decoder.CreatePreviewJpegAsync(imagePath, maxEdge, token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { bytes = null; }
            if (bytes != null) Remember(key, bytes);
            return bytes;
        }
        finally
        {
            gate.Release();
            thumbnailLocks.TryRemove(key, out _);
        }
    }

    /// <summary>Synchronous variant for legacy callers (runs on the thread pool, safe under a UI synchronization context).</summary>
    public byte[]? CreateThumbnail(string imagePath, int maxEdge = 200) => Task.Run(() => CreateThumbnailAsync(imagePath, maxEdge)).GetAwaiter().GetResult();

    private static string CacheKey(string path, int maxEdge)
    {
        long stamp = 0;
        try { stamp = File.GetLastWriteTimeUtc(path).Ticks; } catch (IOException) { }
        return $"{maxEdge}|{stamp}|{path}";
    }

    private void Remember(string key, byte[] bytes)
    {
        if (thumbnailCache.TryAdd(key, bytes)) cacheOrder.Enqueue(key);
        while (thumbnailCache.Count > MaxCachedThumbnails && cacheOrder.TryDequeue(out var oldest)) thumbnailCache.TryRemove(oldest, out _);
    }

    public void InvalidateThumbnailCache(string imagePath)
    {
        foreach (var key in thumbnailCache.Keys.Where(k => k.EndsWith("|" + imagePath, StringComparison.Ordinal)).ToList())
            thumbnailCache.TryRemove(key, out _);
    }

    public void ClearThumbnailCache() => thumbnailCache.Clear();

    /// <summary>Reads GPS from EXIF (JPEG, PNG, TIFF, HEIC, DNG, RAW …). Returns null if absent or invalid.</summary>
    public static GeoCoordinate? ReadGps(string path)
    {
        try
        {
            var gps = MetadataExtractor.ImageMetadataReader.ReadMetadata(path).OfType<GpsDirectory>().FirstOrDefault();
            return gps != null && gps.TryGetGeoLocation(out var location) ? GeoCoordinate.TryCreate(location.Latitude, location.Longitude) : null;
        }
        catch (Exception ex) when (ex is IOException or MetadataExtractor.ImageProcessingException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Copies an image to a temporary file for processing.</summary>
    public string CreateTempCopy(string sourcePath)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException($"Quelldatei nicht gefunden: {sourcePath}");
        string tempPath = Path.Combine(tempFolder, $"edit_{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}");
        File.Copy(sourcePath, tempPath);
        return tempPath;
    }

    /// <summary>
    /// Writes GPS coordinates. JPEG: only the EXIF segment is replaced (no re-encoding).
    /// PNG/TIFF: lossless re-encode. Other formats (BMP, HEIC, RAW …) are refused.
    /// The result is verified with an independent reader before the atomic replace.
    /// </summary>
    public void WriteGpsToImage(string imagePath, double latitude, double longitude)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!GpsValidation.IsValid(latitude, longitude))
            throw new ArgumentOutOfRangeException(nameof(latitude), "Ungültige GPS-Koordinaten.");
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            throw new FileNotFoundException($"Bilddatei nicht gefunden: {imagePath}");

        try
        {
            byte[] original = File.ReadAllBytes(imagePath);
            var detected = ImageFormatDetector.Detect(original.AsSpan(0, Math.Min(32, original.Length)), imagePath);
            if (!ImageFormatCapabilities.CanWriteGps(detected))
                throw new NotSupportedException($"Das Format {ImageFormatCapabilities.DisplayName(detected)} kann hier keine GPS-Metadaten speichern. " +
                    "Für HEIC/RAW die Metadaten als XMP-Sidecar speichern oder zuerst als JPEG/PNG/TIFF exportieren.");

            var info = Image.Identify(original);
            var format = info.Metadata.DecodedImageFormat;
            byte[] output;
            if (format is JpegFormat)
            {
                var exif = info.Metadata.ExifProfile ?? new ExifProfile();
                SetGps(exif, latitude, longitude);
                output = JpegExifWriter.ReplaceExif(original, exif.ToByteArray() ?? throw new InvalidDataException("EXIF konnte nicht serialisiert werden."));
            }
            else if (format is PngFormat or TiffFormat)
            {
                using var image = Image.Load(original);
                var exif = image.Metadata.ExifProfile ?? new ExifProfile();
                SetGps(exif, latitude, longitude);
                image.Metadata.ExifProfile = exif;
                using var stream = new MemoryStream();
                image.Save(stream, format);
                output = stream.ToArray();
            }
            else
            {
                throw new NotSupportedException($"Das Format {format?.Name ?? Path.GetExtension(imagePath)} kann keine GPS-Metadaten speichern.");
            }

            // Verify with an independent reader before touching the target file.
            var gps = MetadataExtractor.ImageMetadataReader.ReadMetadata(new MemoryStream(output))
                .OfType<GpsDirectory>().FirstOrDefault();
            if (gps == null || !gps.TryGetGeoLocation(out var check) ||
                Math.Abs(check.Latitude - latitude) > 1e-6 || Math.Abs(check.Longitude - longitude) > 1e-6)
                throw new InvalidDataException("GPS-Nachprüfung fehlgeschlagen; Datei wurde nicht verändert.");

            AtomicFile.Write(imagePath, output);
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new InvalidOperationException($"Fehler beim Schreiben der GPS-Daten: {ex.Message}", ex);
        }
    }

    private static void SetGps(ExifProfile exif, double latitude, double longitude)
    {
        exif.SetValue(ExifTag.GPSLatitudeRef, latitude >= 0 ? "N" : "S");
        exif.SetValue(ExifTag.GPSLongitudeRef, longitude >= 0 ? "E" : "W");
        exif.SetValue(ExifTag.GPSLatitude, ConvertToRational(Math.Abs(latitude)));
        exif.SetValue(ExifTag.GPSLongitude, ConvertToRational(Math.Abs(longitude)));
    }

    private static Rational[] ConvertToRational(double value)
    {
        int degrees = (int)value;
        double minutesDecimal = (value - degrees) * 60;
        int minutes = (int)minutesDecimal;
        double seconds = (minutesDecimal - minutes) * 60;
        return [new Rational((uint)degrees, 1), new Rational((uint)minutes, 1), new Rational((uint)Math.Round(seconds * 1000000), 1000000)];
    }

    /// <summary>Saves an edited image under a collision-free name in the output folder.</summary>
    public string SaveEditedImage(byte[] imageBytes, string originalFileName, string outputFolder)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (imageBytes == null || imageBytes.Length == 0)
            throw new ArgumentException("Bilddaten sind leer", nameof(imageBytes));
        if (string.IsNullOrEmpty(originalFileName) || string.IsNullOrEmpty(Path.GetExtension(originalFileName)))
            throw new ArgumentException("Dateiname ist ungültig", nameof(originalFileName));
        try
        {
            var newPath = AtomicFile.ExportPath(outputFolder, originalFileName);
            AtomicFile.Write(newPath, imageBytes, overwrite: false);
            return newPath;
        }
        catch (Exception ex)
        {
            throw new IOException($"Fehler beim Speichern des bearbeiteten Bildes: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Copies an image collision-free into the output folder and optionally writes GPS.
    /// The original stays untouched; on failure no half-written copy is left behind.
    /// </summary>
    public string SaveSingleImage(string sourcePath, string outputFolder, double? latitude = null, double? longitude = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException($"Quelldatei nicht gefunden: {sourcePath}");

        var newPath = AtomicFile.ExportPath(outputFolder, Path.GetFileName(sourcePath));
        try
        {
            AtomicFile.Write(newPath, File.ReadAllBytes(sourcePath), overwrite: false);
            if (latitude.HasValue && longitude.HasValue)
                WriteGpsToImage(newPath, latitude.Value, longitude.Value);
            return newPath;
        }
        catch (Exception ex)
        {
            try { if (File.Exists(newPath)) File.Delete(newPath); } catch (IOException) { }
            throw new IOException($"Fehler beim Speichern des Bildes: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        for (int i = 0; i < 3; i++)
        {
            try { if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true); break; }
            catch (IOException) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) { break; }
        }
    }
}
