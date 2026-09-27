using PictureGeoExif.Core.Imaging;
using PictureGeoExif.Core.Photos;
using PictureGeoExif.Platform.Mac;
using PictureGeoExif.Platform.Mac.PhotoKit;

namespace PictureGeoExif.Platform.Tests;

public class PhotoKitMappingTests
{
    [Theory]
    [InlineData(0, PhotoLibraryAccessStatus.NotDetermined)]
    [InlineData(1, PhotoLibraryAccessStatus.Restricted)]
    [InlineData(2, PhotoLibraryAccessStatus.Denied)]
    [InlineData(3, PhotoLibraryAccessStatus.Authorized)]
    [InlineData(4, PhotoLibraryAccessStatus.Limited)]
    [InlineData(99, PhotoLibraryAccessStatus.Denied)] // unknown future values fail safe
    public void AuthorizationStatus_IsMapped(long raw, PhotoLibraryAccessStatus expected) =>
        Assert.Equal(expected, PhotoKitMapping.MapAuthorizationStatus(raw));

    [Fact]
    public void CanRead_OnlyForAuthorizedAndLimited()
    {
        Assert.True(PhotoLibraryAccessStatus.Authorized.CanRead());
        Assert.True(PhotoLibraryAccessStatus.Limited.CanRead());
        Assert.False(PhotoLibraryAccessStatus.Denied.CanRead());
        Assert.False(PhotoLibraryAccessStatus.NotDetermined.CanRead());
        Assert.True(PhotoLibraryAccessStatus.NotDetermined.CanRequest());
    }

    [Fact]
    public void Asset_IsMapped_WithPrimaryResourceAndLocation()
    {
        var asset = PhotoKitMapping.MapAsset(new PhotoKitAssetRecord("A/L0/001", 1, 4 | 8, 4032, 3024, 1_700_000_000.5, null, true, 52.5, 13.4,
            [new(0, 5, "FullSizeRender.jpg", "public.jpeg"), new(1, 1, "IMG_0001.HEIC", "public.heic"), new(2, 9, "IMG_0001.MOV", "com.apple.quicktime-movie")]));
        Assert.Equal("A/L0/001", asset.Id);
        Assert.Equal(PhotoMediaKind.Image, asset.MediaKind);
        Assert.Equal(PhotoSubtypes.Screenshot | PhotoSubtypes.LivePhoto, asset.Subtypes);
        Assert.Equal("IMG_0001.HEIC", asset.OriginalFileName);
        Assert.Equal(ImageFormat.Heic, asset.Format);
        Assert.True(asset.IsFavorite);
        Assert.Equal(52.5, asset.Location!.Value.Latitude);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_500), asset.CreationDate);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-180, 400)]
    public void MissingOrInvalidLocation_IsNull(double lat, double lon) =>
        Assert.Null(PhotoKitMapping.MapAsset(FakePhotoKitFacade.Asset("x", lat: lat, lon: lon)).Location);

    [Fact]
    public void RawResource_SetsRawSubtype_AndOriginalPrefersPhotoOverEdit()
    {
        var resources = new PhotoKitResourceRecord[] { new(0, 5, "edit.jpg", "public.jpeg"), new(1, 4, "IMG.DNG", "com.adobe.raw-image"), new(2, 1, "IMG.JPG", "public.jpeg") };
        Assert.True(PhotoKitMapping.MapSubtypes(0, resources).HasFlag(PhotoSubtypes.Raw));
        Assert.Equal(2, PhotoKitMapping.OriginalResource(resources)!.Index);
        Assert.Equal(1, PhotoKitMapping.OriginalResource([resources[0], resources[1]])!.Index); // RAW alternate before edit
        Assert.Equal(0, PhotoKitMapping.OriginalResource([resources[0]])!.Index);                // only the edit exists
        Assert.Null(PhotoKitMapping.OriginalResource([new PhotoKitResourceRecord(0, 2, "v.mov", "com.apple.quicktime-movie")]));
    }

    [Fact]
    public void Albums_LibraryAndFavoritesFirst_HiddenExcluded()
    {
        var albums = PhotoKitMapping.BuildAlbumList(
        [
            new("s1", "Selfies", 2, 210, 5),
            new("u2", "Urlaub", 1, 2, 12),
            new("h", "Ausgeblendet", 2, PhotoKitMapping.SubtypeSmartAllHidden, 3),
            new("f", "Favoriten", 2, PhotoKitMapping.SubtypeSmartFavorites, 3),
            new("u1", "Arbeit", 1, 2, PhotoKitMapping.NotFound),
            new("sh", "Familie", 1, PhotoKitMapping.SubtypeAlbumCloudShared, 7),
        ]);
        Assert.Equal([PhotoAlbum.LibraryId, PhotoAlbum.FavoritesId, "u1", "sh", "u2", "s1"], albums.Select(a => a.Id));
        Assert.Null(albums.Single(a => a.Id == "u1").EstimatedCount);
        Assert.Equal(PhotoAlbumKind.SharedAlbum, albums.Single(a => a.Id == "sh").Kind);
        Assert.Equal(PhotoAlbumKind.SmartAlbum, albums.Single(a => a.Id == "s1").Kind);
    }

    [Fact]
    public void Query_MapsPseudoAlbumsAndMediaKinds()
    {
        var favorites = PhotoKitMapping.MapQuery(new PhotoQuery { AlbumId = PhotoAlbum.FavoritesId });
        Assert.Null(favorites.CollectionId);
        Assert.True(favorites.FavoritesOnly);
        Assert.Equal([1L], favorites.MediaTypes);
        var album = PhotoKitMapping.MapQuery(new PhotoQuery { AlbumId = "abc", MediaKinds = PhotoMediaKind.Image | PhotoMediaKind.Video, NewestFirst = false });
        Assert.Equal("abc", album.CollectionId);
        Assert.Equal([1L, 2L], album.MediaTypes);
        Assert.False(album.NewestFirst);
    }

    [Theory]
    [InlineData("PHPhotosErrorDomain", 3072, PhotoExportError.Cancelled)]
    [InlineData("PHPhotosErrorDomain", 3164, PhotoExportError.CloudUnavailable)]
    [InlineData("NSURLErrorDomain", -1009, PhotoExportError.CloudUnavailable)]
    [InlineData("CloudPhotoLibraryErrorDomain", 1000, PhotoExportError.CloudUnavailable)]
    [InlineData("PHPhotosErrorDomain", 3201, PhotoExportError.AssetNotFound)]
    [InlineData("PHPhotosErrorDomain", 3303, PhotoExportError.NoOriginalResource)]
    [InlineData("PHPhotosErrorDomain", 3311, PhotoExportError.NotAuthorized)]
    [InlineData("SomethingElse", 1, PhotoExportError.Unknown)]
    public void DataErrors_AreMapped(string domain, long code, PhotoExportError expected) =>
        Assert.Equal(expected, PhotoKitMapping.MapDataError(domain, code));

    [Theory]
    [InlineData("/Applications/PictureGeoExif.app/Contents/MacOS/", "/Applications/PictureGeoExif.app")]
    [InlineData("/Users/x/dev/bin/Release/net10.0/osx-arm64/publish/", null)]
    [InlineData("/tmp/Contents/MacOS/", null)]
    [InlineData(@"C:\Build\PictureGeoExif.app\Contents\MacOS\", @"C:\Build\PictureGeoExif.app")]
    public void AppBundle_IsDetectedFromExecutableFolder(string baseDirectory, string? expected) =>
        Assert.Equal(expected, MacPlatformInfo.FindBundle(baseDirectory));
}
