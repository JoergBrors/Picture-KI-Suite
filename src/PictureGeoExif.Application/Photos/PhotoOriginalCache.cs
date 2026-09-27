using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using PictureGeoExif.Core.IO;
using PictureGeoExif.Core.Photos;

namespace PictureGeoExif.Application.Photos;

/// <summary>
/// Photos asset → original resource → local file → shared metadata core.
/// Keeps read-only local copies of Photos originals so the existing EXIF/GPS/XMP code can analyse them unchanged.
/// The Photos library itself is never touched; folders are named by a hash, never by the PhotoKit id.
/// </summary>
public sealed class PhotoOriginalCache(IPhotoLibraryService photos, string cacheFolder, ILogger<PhotoOriginalCache>? logger = null)
{
    public string CacheFolder => cacheFolder;

    public string PathFor(string assetId, string? originalFileName)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(assetId)))[..24].ToLowerInvariant();
        return Path.Combine(cacheFolder, hash, AtomicFile.SafeFileName(originalFileName, "original"));
    }

    public bool TryGetCached(string assetId, string? originalFileName, out string path)
    {
        path = PathFor(assetId, originalFileName);
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    /// <summary>Returns a local path of the unmodified original, downloading it from iCloud if required.</summary>
    public async Task<PhotoExportResult> MaterializeAsync(string assetId, string? originalFileName, IProgress<PhotoTransferProgress>? progress, CancellationToken token)
    {
        if (TryGetCached(assetId, originalFileName, out var cached))
            return PhotoExportResult.Ok(cached, originalFileName, new FileInfo(cached).Length);
        string target = PathFor(assetId, originalFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var result = await photos.ExportOriginalAsync(assetId, target, progress, token);
        if (!result.Success) logger?.LogWarning("Original konnte nicht geladen werden: {Error} {Message}", result.Error, result.ErrorMessage);
        return result;
    }

    /// <summary>Deletes all cached originals (e.g. from the diagnostics window).</summary>
    public long Clear()
    {
        if (!Directory.Exists(cacheFolder)) return 0;
        long freed = 0;
        foreach (var file in Directory.EnumerateFiles(cacheFolder, "*", SearchOption.AllDirectories))
        {
            try { freed += new FileInfo(file).Length; File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        try { Directory.Delete(cacheFolder, true); } catch (IOException) { }
        return freed;
    }
}
