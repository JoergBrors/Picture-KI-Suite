using Microsoft.Extensions.Logging;
using PictureGeoExif.Core.Imaging;
using PictureGeoExif.Core.IO;
using PictureGeoExif.Core.Photos;
using PictureGeoExif.Platform.Mac.PhotoKit;

namespace PictureGeoExif.Platform.Mac;

/// <summary>
/// <see cref="IPhotoLibraryService"/> for Apple Photos. Read-only in phase 1: it lists, renders thumbnails and
/// exports originals to new files; it never changes assets or the library.
/// All PhotoKit work runs off the UI thread; thumbnail rendering is limited to a few parallel requests.
/// </summary>
public sealed class MacPhotoLibraryService(IPhotoKitFacade facade, ILogger<MacPhotoLibraryService>? logger = null) : IPhotoLibraryService
{
    private readonly SemaphoreSlim thumbnailGate = new(4);

    public bool IsAvailable => facade.IsAvailable;

    public Task<PhotoLibraryAccessStatus> GetAuthorizationStatusAsync()
    {
        if (!facade.IsAvailable) return Task.FromResult(PhotoLibraryAccessStatus.Unavailable);
        try { return Task.FromResult(PhotoKitMapping.MapAuthorizationStatus(facade.AuthorizationStatus())); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger?.LogError(ex, "PhotoKit-Status konnte nicht gelesen werden.");
            return Task.FromResult(PhotoLibraryAccessStatus.Unavailable);
        }
    }

    public async Task<PhotoLibraryAccessStatus> RequestAuthorizationAsync()
    {
        var current = await GetAuthorizationStatusAsync();
        if (current != PhotoLibraryAccessStatus.NotDetermined) return current;
        try
        {
            var status = PhotoKitMapping.MapAuthorizationStatus(await facade.RequestAuthorizationAsync());
            logger?.LogInformation("Photo Library Authorization Status nach Anfrage: {Status}", status);
            return status;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger?.LogError(ex, "PhotoKit-Berechtigungsanfrage fehlgeschlagen.");
            return await GetAuthorizationStatusAsync();
        }
    }

    private async Task<bool> CanReadAsync() => (await GetAuthorizationStatusAsync()).CanRead();

    public async Task<IReadOnlyList<PhotoAlbum>> GetAlbumsAsync(CancellationToken cancellationToken = default)
    {
        if (!await CanReadAsync()) return [];
        var collections = await Task.Run(facade.FetchCollections, cancellationToken);
        return PhotoKitMapping.BuildAlbumList(collections);
    }

    public async Task<IReadOnlyList<PhotoAsset>> GetAssetsAsync(PhotoQuery query, CancellationToken cancellationToken = default)
    {
        if (!await CanReadAsync()) return [];
        var mapped = PhotoKitMapping.MapQuery(query);
        int limit = Math.Clamp(query.Limit, 1, 1000);
        var records = await Task.Run(() => facade.FetchAssets(mapped, Math.Max(0, query.Offset), limit), cancellationToken);
        return records.Select(PhotoKitMapping.MapAsset).ToList();
    }

    public async Task<int> CountAssetsAsync(PhotoQuery query, CancellationToken cancellationToken = default)
    {
        if (!await CanReadAsync()) return 0;
        var mapped = PhotoKitMapping.MapQuery(query);
        return await Task.Run(() => facade.CountAssets(mapped), cancellationToken);
    }

    public async Task<PhotoAssetDetails?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default)
    {
        if (!await CanReadAsync()) return null;
        var record = await Task.Run(() => facade.FetchAsset(assetId), cancellationToken);
        return record == null ? null : new PhotoAssetDetails(PhotoKitMapping.MapAsset(record), record.Resources.Select(PhotoKitMapping.MapResource).ToList());
    }

    public async Task<byte[]?> GetThumbnailAsync(string assetId, int maxPixelSize, CancellationToken cancellationToken = default)
    {
        await thumbnailGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await Task.Run(() => facade.RequestThumbnailJpeg(assetId, maxPixelSize), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            logger?.LogWarning(ex, "Thumbnail konnte nicht erzeugt werden.");
            return null;
        }
        finally { thumbnailGate.Release(); }
    }

    public async Task<Stream?> OpenOriginalAsync(string assetId, CancellationToken cancellationToken = default)
    {
        string folder = Path.Combine(Path.GetTempPath(), "PictureGeoExif-photos", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var details = await GetAssetAsync(assetId, cancellationToken);
        string target = Path.Combine(folder, AtomicFile.SafeFileName(details?.OriginalResource?.OriginalFileName, "original"));
        var result = await ExportOriginalAsync(assetId, target, null, cancellationToken);
        if (!result.Success) { TryDeleteFolder(folder); return null; }
        // Deleted automatically when the caller disposes the stream.
        return new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
    }

    public async Task<PhotoExportResult> ExportOriginalAsync(string assetId, string destinationPath, IProgress<PhotoTransferProgress>? progress, CancellationToken cancellationToken = default)
    {
        if (!facade.IsAvailable) return PhotoExportResult.Fail(PhotoExportError.NotAuthorized, "Photos.framework nicht verfügbar.");
        if (!await CanReadAsync()) return PhotoExportResult.Fail(PhotoExportError.NotAuthorized, "Kein Zugriff auf die Fotomediathek.");
        string target = Path.GetFullPath(destinationPath);
        if (File.Exists(target)) return PhotoExportResult.Fail(PhotoExportError.DestinationExists, "Zieldatei existiert bereits.");

        PhotoKitAssetRecord? record;
        try { record = await Task.Run(() => facade.FetchAsset(assetId), cancellationToken); }
        catch (OperationCanceledException) { return PhotoExportResult.Fail(PhotoExportError.Cancelled, "Abgebrochen."); }
        if (record == null) return PhotoExportResult.Fail(PhotoExportError.AssetNotFound, "Asset nicht gefunden.");
        var resource = PhotoKitMapping.OriginalResource(record.Resources);
        if (resource == null) return PhotoExportResult.Fail(PhotoExportError.NoOriginalResource, "Keine Originalressource.");

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".download";
        long bytes = 0;
        try
        {
            PhotoKitDataResult data;
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920))
            {
                var fraction = progress == null ? null : new Progress<double>(f => progress.Report(new PhotoTransferProgress(f, bytes, FromCloud: true)));
                data = await facade.RequestResourceDataAsync(assetId, resource.Index, stream, fraction, cancellationToken);
                bytes = data.Bytes;
                if (data.Success) stream.Flush(true);
            }
            if (!data.Success)
            {
                var error = data.Cancelled ? PhotoExportError.Cancelled : PhotoKitMapping.MapDataError(data.ErrorDomain, data.ErrorCode);
                logger?.LogWarning("Original-Export fehlgeschlagen: {Domain} {Code} {Message}", data.ErrorDomain, data.ErrorCode, data.ErrorMessage);
                return PhotoExportResult.Fail(error, data.ErrorMessage ?? error.ToString());
            }
            if (bytes == 0) return PhotoExportResult.Fail(PhotoExportError.NoOriginalResource, "Leere Originaldaten.");
            // Sanity check: the bytes must look like the declared format (HEIC is still HEIC, not a rendered JPEG).
            var detected = ImageFormatDetector.DetectFile(temporary);
            logger?.LogInformation("Original exportiert: {Format}, {Bytes} Bytes", ImageFormatCapabilities.DisplayName(detected), bytes);
            AtomicFile.Commit(temporary, target, overwrite: false);
            progress?.Report(new PhotoTransferProgress(1, bytes, FromCloud: false));
            return PhotoExportResult.Ok(target, resource.OriginalFilename, bytes);
        }
        catch (OperationCanceledException) { return PhotoExportResult.Fail(PhotoExportError.Cancelled, "Abgebrochen."); }
        catch (IOException ex)
        {
            logger?.LogWarning(ex, "Original konnte nicht geschrieben werden.");
            return PhotoExportResult.Fail(File.Exists(target) ? PhotoExportError.DestinationExists : PhotoExportError.IoError, ex.Message);
        }
        catch (UnauthorizedAccessException ex) { return PhotoExportResult.Fail(PhotoExportError.IoError, ex.Message); }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } }
    }

    private static void TryDeleteFolder(string folder)
    {
        try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
