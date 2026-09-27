using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PictureGeoExif.Core.Photos;
using PictureGeoExif.Platform.Mac.Interop;

namespace PictureGeoExif.Platform.Mac.PhotoKit;

/// <summary>
/// PhotoKit through the Objective-C runtime. Uses only public API of Photos.framework (PHPhotoLibrary, PHAsset,
/// PHAssetCollection, PHFetchOptions, PHFetchResult, PHImageManager, PHAssetResource, PHAssetResourceManager).
/// Never touches the .photoslibrary package on disk. Every public method runs inside its own autorelease pool and
/// only returns managed copies of the data, so no Objective-C object outlives a call.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class ObjCPhotoKitFacade : IPhotoKitFacade
{
    private static readonly nint NSIntegerMax = nint.MaxValue;
    private const nint DeliveryModeHighQuality = 1, ResizeModeFast = 1, ContentModeAspectFit = 0;
    private const nuint BitmapFileTypeJpeg = 3;

    private readonly bool available;

    public ObjCPhotoKitFacade()
    {
        try
        {
            available = OperatingSystem.IsMacOS() && ObjC.LoadFramework("Photos") && ObjC.LoadFramework("AppKit") && ObjC.HasClass("PHPhotoLibrary");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { available = false; }
    }

    public bool IsAvailable => available;

    private void EnsureAvailable()
    {
        if (!available) throw new PlatformNotSupportedException("Photos.framework ist nicht verfügbar.");
    }

    // ---------------- Authorization ----------------

    public long AuthorizationStatus()
    {
        EnsureAvailable();
        using var pool = AutoreleasePool.Create();
        return ObjC.SendNint(ObjC.Class("PHPhotoLibrary"), "authorizationStatusForAccessLevel:", (nint)PhotoKitMapping.AccessLevelReadWrite);
    }

    public unsafe Task<long> RequestAuthorizationAsync()
    {
        EnsureAvailable();
        var completion = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        long id = ObjCBlock.Register(completion);
        IntPtr block = ObjCBlock.Create((IntPtr)(delegate* unmanaged<IntPtr, nint, void>)&AuthorizationInvoke, id);
        try
        {
            using var pool = AutoreleasePool.Create();
            ObjC.SendN(ObjC.Class("PHPhotoLibrary"), "requestAuthorizationForAccessLevel:handler:", (nint)PhotoKitMapping.AccessLevelReadWrite, block);
        }
        catch
        {
            ObjCBlock.Unregister(id);
            throw;
        }
        finally { ObjCBlock.Free(block); }
        return completion.Task;
    }

    [UnmanagedCallersOnly]
    private static void AuthorizationInvoke(IntPtr block, nint status)
    {
        try
        {
            if (ObjCBlock.State<TaskCompletionSource<long>>(block) is { } completion)
            {
                completion.TrySetResult(status);
                // The handler is called exactly once.
                UnregisterFromBlock(block);
            }
        }
        catch { /* never let exceptions cross into Objective-C */ }
    }

    private static void UnregisterFromBlock(IntPtr block) => ObjCBlock.Unregister(ObjCBlock.ContextOf(block));

    // ---------------- Collections ----------------

    public IReadOnlyList<PhotoKitCollectionRecord> FetchCollections()
    {
        EnsureAvailable();
        using var pool = AutoreleasePool.Create();
        var result = new List<PhotoKitCollectionRecord>();
        foreach (long type in new[] { PhotoKitMapping.CollectionTypeAlbum, PhotoKitMapping.CollectionTypeSmartAlbum })
        {
            IntPtr fetch = ObjC.SendN(ObjC.Class("PHAssetCollection"), "fetchAssetCollectionsWithType:subtype:options:", (nint)type, NSIntegerMax, IntPtr.Zero);
            nuint count = ObjC.SendNuint(fetch, "count");
            for (nuint i = 0; i < count; i++)
            {
                IntPtr collection = ObjC.SendN(fetch, "objectAtIndex:", (nint)i);
                result.Add(new PhotoKitCollectionRecord(
                    ObjC.FromNSString(ObjC.Send(collection, "localIdentifier")) ?? "",
                    ObjC.FromNSString(ObjC.Send(collection, "localizedTitle")),
                    type,
                    ObjC.SendNint(collection, "assetCollectionSubtype"),
                    (long)Math.Min((ulong)ObjC.SendNuint(collection, "estimatedAssetCount"), (ulong)PhotoKitMapping.NotFound)));
            }
        }
        return result;
    }

    // ---------------- Assets ----------------

    /// <summary>Returns an autoreleased PHFetchResult for the query, or zero if the collection no longer exists.</summary>
    private static IntPtr FetchResult(PhotoKitAssetQuery query)
    {
        IntPtr options = ObjC.New("PHFetchOptions");
        try
        {
            IntPtr sort = ObjC.SendBool(ObjC.Class("NSSortDescriptor"), "sortDescriptorWithKey:ascending:", ObjC.NSString("creationDate"), query.NewestFirst ? (byte)0 : (byte)1);
            ObjC.SendVoid(options, "setSortDescriptors:", ObjC.NSArrayWith(sort));
            string media = string.Join(" OR ", query.MediaTypes.Select(t => $"mediaType == {t}"));
            string format = query.FavoritesOnly ? $"({media}) AND favorite == YES" : $"({media})";
            // predicateWithFormat:argumentArray: is not variadic, therefore safe to call through objc_msgSend.
            IntPtr predicate = ObjC.Send(ObjC.Class("NSPredicate"), "predicateWithFormat:argumentArray:", ObjC.NSString(format), IntPtr.Zero);
            ObjC.SendVoid(options, "setPredicate:", predicate);

            if (query.CollectionId is not { Length: > 0 } collectionId)
                return ObjC.Send(ObjC.Class("PHAsset"), "fetchAssetsWithOptions:", options);
            IntPtr collections = ObjC.Send(ObjC.Class("PHAssetCollection"), "fetchAssetCollectionsWithLocalIdentifiers:options:",
                ObjC.NSArrayWith(ObjC.NSString(collectionId)), IntPtr.Zero);
            IntPtr collection = ObjC.Send(collections, "firstObject");
            return collection == IntPtr.Zero ? IntPtr.Zero : ObjC.Send(ObjC.Class("PHAsset"), "fetchAssetsInAssetCollection:options:", collection, options);
        }
        finally { ObjC.objc_release(options); }
    }

    public int CountAssets(PhotoKitAssetQuery query)
    {
        EnsureAvailable();
        using var pool = AutoreleasePool.Create();
        IntPtr fetch = FetchResult(query);
        return fetch == IntPtr.Zero ? 0 : (int)Math.Min(int.MaxValue, (ulong)ObjC.SendNuint(fetch, "count"));
    }

    public IReadOnlyList<PhotoKitAssetRecord> FetchAssets(PhotoKitAssetQuery query, int offset, int limit)
    {
        EnsureAvailable();
        using var pool = AutoreleasePool.Create();
        IntPtr fetch = FetchResult(query);
        if (fetch == IntPtr.Zero) return [];
        long count = (long)ObjC.SendNuint(fetch, "count");
        var result = new List<PhotoKitAssetRecord>();
        for (long i = Math.Max(0, offset); i < count && result.Count < limit; i++)
        {
            // A pool per asset keeps memory flat on large pages (resources, dates, strings are autoreleased).
            using var inner = AutoreleasePool.Create();
            result.Add(ReadAsset(ObjC.SendN(fetch, "objectAtIndex:", (nint)i)));
        }
        return result;
    }

    public PhotoKitAssetRecord? FetchAsset(string localIdentifier)
    {
        EnsureAvailable();
        using var pool = AutoreleasePool.Create();
        IntPtr asset = FindAsset(localIdentifier);
        return asset == IntPtr.Zero ? null : ReadAsset(asset);
    }

    private static IntPtr FindAsset(string localIdentifier)
    {
        IntPtr fetch = ObjC.Send(ObjC.Class("PHAsset"), "fetchAssetsWithLocalIdentifiers:options:", ObjC.NSArrayWith(ObjC.NSString(localIdentifier)), IntPtr.Zero);
        return ObjC.Send(fetch, "firstObject");
    }

    private static PhotoKitAssetRecord ReadAsset(IntPtr asset)
    {
        double? lat = null, lon = null;
        IntPtr location = ObjC.Send(asset, "location");
        if (location != IntPtr.Zero)
        {
            var coordinate = ObjC.SendCoordinate(location, "coordinate");
            lat = coordinate.First; lon = coordinate.Second;
        }
        return new PhotoKitAssetRecord(
            ObjC.FromNSString(ObjC.Send(asset, "localIdentifier")) ?? "",
            ObjC.SendNint(asset, "mediaType"),
            ObjC.SendNuint(asset, "mediaSubtypes"),
            (long)ObjC.SendNuint(asset, "pixelWidth"),
            (long)ObjC.SendNuint(asset, "pixelHeight"),
            ObjC.UnixTime(ObjC.Send(asset, "creationDate")),
            ObjC.UnixTime(ObjC.Send(asset, "modificationDate")),
            ObjC.SendByte(asset, "isFavorite") != 0,
            lat, lon,
            ReadResources(asset));
    }

    private static IReadOnlyList<PhotoKitResourceRecord> ReadResources(IntPtr asset)
    {
        IntPtr resources = ObjC.Send(ObjC.Class("PHAssetResource"), "assetResourcesForAsset:", asset);
        nuint count = resources == IntPtr.Zero ? 0 : ObjC.SendNuint(resources, "count");
        var result = new List<PhotoKitResourceRecord>((int)count);
        for (nuint i = 0; i < count; i++)
        {
            IntPtr resource = ObjC.SendN(resources, "objectAtIndex:", (nint)i);
            result.Add(new PhotoKitResourceRecord((int)i, ObjC.SendNint(resource, "type"),
                ObjC.FromNSString(ObjC.Send(resource, "originalFilename")),
                ObjC.FromNSString(ObjC.Send(resource, "uniformTypeIdentifier"))));
        }
        return result;
    }

    // ---------------- Thumbnails ----------------

    private sealed class ThumbnailState { public byte[]? Jpeg; }

    public unsafe byte[]? RequestThumbnailJpeg(string localIdentifier, int maxPixelSize)
    {
        EnsureAvailable();
        using var pool = AutoreleasePool.Create();
        IntPtr asset = FindAsset(localIdentifier);
        if (asset == IntPtr.Zero) return null;
        IntPtr options = ObjC.New("PHImageRequestOptions");
        var state = new ThumbnailState();
        long id = ObjCBlock.Register(state);
        IntPtr block = ObjCBlock.Create((IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&ThumbnailInvoke, id);
        try
        {
            // Synchronous on this background thread: the handler runs exactly once before the call returns.
            ObjC.SendVoidByte(options, "setSynchronous:", 1);
            ObjC.SendVoidN(options, "setDeliveryMode:", DeliveryModeHighQuality);
            ObjC.SendVoidN(options, "setResizeMode:", ResizeModeFast);
            // Lists must never trigger iCloud downloads; Photos keeps local thumbnails for cloud-only originals.
            ObjC.SendVoidByte(options, "setNetworkAccessAllowed:", 0);
            IntPtr manager = ObjC.Send(ObjC.Class("PHImageManager"), "defaultManager");
            double edge = Math.Clamp(maxPixelSize, 16, 4096);
            ObjC.SendImageRequest(manager, asset, new SizePair(edge, edge), ContentModeAspectFit, options, block);
            return state.Jpeg;
        }
        finally
        {
            ObjCBlock.Unregister(id);
            ObjCBlock.Free(block);
            ObjC.objc_release(options);
        }
    }

    [UnmanagedCallersOnly]
    private static void ThumbnailInvoke(IntPtr block, IntPtr image, IntPtr info)
    {
        try
        {
            if (image == IntPtr.Zero || ObjCBlock.State<ThumbnailState>(block) is not { } state) return;
            IntPtr pool = ObjC.PushPool();
            try { state.Jpeg = ImageToJpeg(image); }
            finally { ObjC.PopPool(pool); }
        }
        catch { /* never let exceptions cross into Objective-C */ }
    }

    /// <summary>NSImage → TIFF → NSBitmapImageRep → JPEG bytes.</summary>
    private static byte[]? ImageToJpeg(IntPtr image)
    {
        IntPtr tiff = ObjC.Send(image, "TIFFRepresentation");
        if (tiff == IntPtr.Zero) return null;
        IntPtr rep = ObjC.Send(ObjC.Class("NSBitmapImageRep"), "imageRepWithData:", tiff);
        if (rep == IntPtr.Zero) return null;
        IntPtr jpeg = ObjC.SendRepresentation(rep, BitmapFileTypeJpeg, ObjC.Send(ObjC.Class("NSDictionary"), "dictionary"));
        return jpeg == IntPtr.Zero ? null : ObjC.NSDataBytes(jpeg);
    }

    // ---------------- Original data ----------------

    private sealed class DataRequestState(Stream destination, IProgress<double>? progress)
    {
        public readonly Stream Destination = destination;
        public readonly IProgress<double>? Progress = progress;
        public readonly TaskCompletionSource<PhotoKitDataResult> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long Bytes;
        public Exception? WriteError;
        public volatile bool Cancelled;
    }

    public async Task<PhotoKitDataResult> RequestResourceDataAsync(string localIdentifier, int resourceIndex, Stream destination,
        IProgress<double>? progress, CancellationToken token)
    {
        EnsureAvailable();
        token.ThrowIfCancellationRequested();
        var state = new DataRequestState(destination, progress);
        long id = ObjCBlock.Register(state);
        try
        {
            var (requestId, immediate) = StartDataRequest(localIdentifier, resourceIndex, id);
            if (immediate != null) return immediate;
            await using var registration = token.Register(() =>
            {
                state.Cancelled = true;
                using (AutoreleasePool.Create())
                    ObjC.SendVoidInt(ObjC.Send(ObjC.Class("PHAssetResourceManager"), "defaultManager"), "cancelDataRequest:", requestId);
                // Do not wait for PhotoKit's completion callback; late data callbacks are ignored because Cancelled is set.
                state.Completion.TrySetResult(new PhotoKitDataResult(false, true, state.Bytes));
            });
            return await state.Completion.Task.ConfigureAwait(false);
        }
        finally { ObjCBlock.Unregister(id); }
    }

    /// <summary>Starts requestDataForAssetResource:… and returns its request id, or an immediate failure.</summary>
    private static unsafe (int RequestId, PhotoKitDataResult? Immediate) StartDataRequest(string localIdentifier, int resourceIndex, long id)
    {
        IntPtr options = IntPtr.Zero, dataBlock = IntPtr.Zero, completionBlock = IntPtr.Zero, progressBlock = IntPtr.Zero;
        using var pool = AutoreleasePool.Create();
        try
        {
            IntPtr asset = FindAsset(localIdentifier);
            if (asset == IntPtr.Zero)
                return (0, new PhotoKitDataResult(false, false, 0, "PHPhotosErrorDomain", PhotoKitMapping.ErrorIdentifierNotFound, "Asset nicht gefunden."));
            IntPtr resources = ObjC.Send(ObjC.Class("PHAssetResource"), "assetResourcesForAsset:", asset);
            if (resources == IntPtr.Zero || resourceIndex < 0 || (nuint)resourceIndex >= ObjC.SendNuint(resources, "count"))
                return (0, new PhotoKitDataResult(false, false, 0, "PHPhotosErrorDomain", PhotoKitMapping.ErrorMissingResource, "Ressource nicht gefunden."));
            IntPtr resource = ObjC.SendN(resources, "objectAtIndex:", resourceIndex);

            options = ObjC.New("PHAssetResourceRequestOptions");
            // Allows downloading the original from iCloud; progress is reported through the progress handler.
            ObjC.SendVoidByte(options, "setNetworkAccessAllowed:", 1);
            progressBlock = ObjCBlock.Create((IntPtr)(delegate* unmanaged<IntPtr, double, void>)&ProgressInvoke, id);
            ObjC.SendVoid(options, "setProgressHandler:", progressBlock);

            dataBlock = ObjCBlock.Create((IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&DataInvoke, id);
            completionBlock = ObjCBlock.Create((IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&CompletionInvoke, id);
            IntPtr manager = ObjC.Send(ObjC.Class("PHAssetResourceManager"), "defaultManager");
            int requestId = ObjC.SendInt(manager, "requestDataForAssetResource:options:dataReceivedHandler:completionHandler:", resource, options, dataBlock, completionBlock);
            return (requestId, null);
        }
        finally
        {
            ObjCBlock.Free(dataBlock);
            ObjCBlock.Free(completionBlock);
            ObjCBlock.Free(progressBlock);
            if (options != IntPtr.Zero) ObjC.objc_release(options);
        }
    }

    [UnmanagedCallersOnly]
    private static unsafe void DataInvoke(IntPtr block, IntPtr data)
    {
        try
        {
            if (ObjCBlock.State<DataRequestState>(block) is not { } state || state.WriteError != null || state.Cancelled || data == IntPtr.Zero) return;
            nuint length = ObjC.SendNuint(data, "length");
            if (length == 0) return;
            var span = new ReadOnlySpan<byte>((void*)ObjC.Send(data, "bytes"), checked((int)length));
            state.Destination.Write(span);
            state.Bytes += (long)length;
        }
        catch (Exception ex)
        {
            try { if (ObjCBlock.State<DataRequestState>(block) is { } state) state.WriteError = ex; } catch { }
        }
    }

    [UnmanagedCallersOnly]
    private static void ProgressInvoke(IntPtr block, double progress)
    {
        try { ObjCBlock.State<DataRequestState>(block)?.Progress?.Report(Math.Clamp(progress, 0, 1)); }
        catch { /* progress is informational */ }
    }

    [UnmanagedCallersOnly]
    private static void CompletionInvoke(IntPtr block, IntPtr error)
    {
        try
        {
            if (ObjCBlock.State<DataRequestState>(block) is not { } state) return;
            IntPtr pool = ObjC.PushPool();
            try
            {
                var (domain, code, message) = ObjC.ErrorInfo(error);
                if (state.WriteError != null)
                    state.Completion.TrySetResult(new PhotoKitDataResult(false, false, state.Bytes, "NSPOSIXErrorDomain", 5, state.WriteError.Message));
                else if (state.Cancelled || PhotoKitMapping.MapDataError(domain, code) == PhotoExportError.Cancelled && error != IntPtr.Zero)
                    state.Completion.TrySetResult(new PhotoKitDataResult(false, true, state.Bytes, domain, code, message));
                else if (error != IntPtr.Zero)
                    state.Completion.TrySetResult(new PhotoKitDataResult(false, false, state.Bytes, domain, code, message));
                else
                    state.Completion.TrySetResult(new PhotoKitDataResult(true, false, state.Bytes));
            }
            finally { ObjC.PopPool(pool); }
        }
        catch { /* never let exceptions cross into Objective-C */ }
    }
}
