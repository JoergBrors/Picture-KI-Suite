using PictureGeoExif.Platform.Mac.PhotoKit;

namespace PictureGeoExif.Platform.Tests;

/// <summary>In-memory PhotoKit: lets the adapter logic run without macOS and without a real Photos library.</summary>
internal sealed class FakePhotoKitFacade : IPhotoKitFacade
{
    public bool IsAvailable { get; set; } = true;
    public bool HasUsageDescription { get; set; } = true;
    public long Status { get; set; } = 3; // authorized
    public long StatusAfterRequest { get; set; } = 3;
    public int RequestCount { get; private set; }
    public List<PhotoKitCollectionRecord> Collections { get; } = [];
    public List<PhotoKitAssetRecord> Assets { get; } = [];
    public Dictionary<(string Id, int Index), byte[]> Data { get; } = [];
    public PhotoKitDataResult? FailWith { get; set; }
    public TaskCompletionSource? Gate { get; set; }
    public PhotoKitAssetQuery? LastQuery { get; private set; }

    public long AuthorizationStatus() => Status;

    public Task<long> RequestAuthorizationAsync()
    {
        RequestCount++;
        Status = StatusAfterRequest;
        return Task.FromResult(Status);
    }

    public IReadOnlyList<PhotoKitCollectionRecord> FetchCollections() => Collections;

    private IEnumerable<PhotoKitAssetRecord> Query(PhotoKitAssetQuery query)
    {
        LastQuery = query;
        return Assets.Where(a => query.MediaTypes.Contains(a.MediaType) && (!query.FavoritesOnly || a.Favorite));
    }

    public int CountAssets(PhotoKitAssetQuery query) => Query(query).Count();

    public IReadOnlyList<PhotoKitAssetRecord> FetchAssets(PhotoKitAssetQuery query, int offset, int limit) => Query(query).Skip(offset).Take(limit).ToList();

    public PhotoKitAssetRecord? FetchAsset(string localIdentifier) => Assets.FirstOrDefault(a => a.LocalIdentifier == localIdentifier);

    public byte[]? RequestThumbnailJpeg(string localIdentifier, int maxPixelSize) => FetchAsset(localIdentifier) == null ? null : [0xFF, 0xD8, 0xFF, 0xD9];

    public async Task<PhotoKitDataResult> RequestResourceDataAsync(string localIdentifier, int resourceIndex, Stream destination, IProgress<double>? progress, CancellationToken token)
    {
        if (Gate != null)
        {
            using var registration = token.Register(() => Gate.TrySetCanceled());
            try { await Gate.Task; }
            catch (TaskCanceledException) { return new PhotoKitDataResult(false, true, 0); }
        }
        if (FailWith != null) return FailWith;
        if (!Data.TryGetValue((localIdentifier, resourceIndex), out var bytes))
            return new PhotoKitDataResult(false, false, 0, "PHPhotosErrorDomain", PhotoKitMapping.ErrorMissingResource, "missing");
        progress?.Report(0.5);
        destination.Write(bytes, 0, bytes.Length / 2);
        destination.Write(bytes, bytes.Length / 2, bytes.Length - bytes.Length / 2);
        progress?.Report(1);
        return new PhotoKitDataResult(true, false, bytes.Length);
    }

    public static PhotoKitAssetRecord Asset(string id, long mediaType = 1, bool favorite = false, double? lat = 48.1, double? lon = 11.5,
        params PhotoKitResourceRecord[] resources) =>
        new(id, mediaType, 0, 4032, 3024, 1_700_000_000, 1_700_000_100, favorite, lat, lon,
            resources.Length > 0 ? resources : [new PhotoKitResourceRecord(0, 1, "IMG_0001.HEIC", "public.heic")]);
}
