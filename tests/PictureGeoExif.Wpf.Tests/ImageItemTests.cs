using PictureExifclone.Models;

namespace PictureGeoExif.Tests;

public class ImageItemTests
{
    [Fact]
    public void ImageItem_UndoRestoresPathAndCoordinates()
    {
        var item = new ImageItem { FilePath = @"C:\a.jpg", Latitude = 1, Longitude = 2 };
        Assert.False(item.CanUndo);
        item.PushHistory();
        item.FilePath = @"D:\out\a_copy.jpg"; item.Latitude = 50; item.Longitude = 8;
        Assert.True(item.CanUndo);
        Assert.Equal(@"D:\out\a_copy.jpg", item.Undo());
        Assert.Equal((@"C:\a.jpg", 1.0, 2.0), (item.FilePath, item.Latitude!.Value, item.Longitude!.Value));
        Assert.False(item.CanUndo);
        Assert.Null(item.Undo());
    }
}
