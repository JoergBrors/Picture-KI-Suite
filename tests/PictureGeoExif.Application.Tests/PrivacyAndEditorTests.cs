using PictureGeoExif.Application.Editing;
using PictureGeoExif.Application.Privacy;
using PictureGeoExif.Core.Geo;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PictureGeoExif.Application.Tests;

public class PrivacyGuardTests
{
    private static readonly PrivacyContext Context = new(["/Users/anna/Pictures/Urlaub/IMG_1.jpg"], ["9F2A0C4E-1111-2222-3333-444455556666/L0/001"], [new GeoCoordinate(52.520008, 13.404954)]);

    [Theory]
    [InlineData("Datei: /Users/anna/Pictures/Urlaub/IMG_1.jpg")]
    [InlineData("Ordner /Users/anna/Pictures/Urlaub enthält")]
    [InlineData("id 9F2A0C4E-1111-2222-3333-444455556666")]
    [InlineData("Position 52.520008, 13.404954")]
    [InlineData("Position 52,5200 / 13,4049")]
    public void ForbiddenData_IsRejected(string payload) =>
        Assert.Throws<PrivacyViolationException>(() => PrivacyGuard.EnsureSafe(payload, Context));

    [Fact]
    public void WindowsFolder_IsRejected() =>
        Assert.Throws<PrivacyViolationException>(() => PrivacyGuard.EnsureSafe(@"Ordner C:\Users\anna\Bilder",
            new PrivacyContext([@"C:\Users\anna\Bilder\a.jpg"], [], [])));

    [Theory]
    [InlineData("Metadaten: {\"captureDate\":\"2024-01-15\",\"hemisphere\":\"Nordhalbkugel\"}")]
    [InlineData("datei IMG_1.jpg, Stichwörter: Schnee")]
    [InlineData("Nur Breite 52.52 ohne Länge")]
    public void MinimalContext_Passes(string payload) => PrivacyGuard.EnsureSafe(payload, Context);
}

public class EditorOperationsTests
{
    [Fact]
    public void Crop_UsesSelection()
    {
        using var image = new Image<Rgba32>(40, 30);
        EditorOperations.Create(new EditorToolSettings { Tool = EditorTool.Crop, Selection = new Rectangle(5, 5, 10, 8) }, 40, 30)(image);
        Assert.Equal((10, 8), (image.Width, image.Height));
    }

    [Theory]
    [InlineData(EditorTool.Crop)]
    [InlineData(EditorTool.Blur)]
    [InlineData(EditorTool.Pixelate)]
    public void AreaTools_RequireSelection(EditorTool tool) =>
        Assert.Throws<InvalidOperationException>(() => EditorOperations.Create(new EditorToolSettings { Tool = tool }, 10, 10));

    [Fact]
    public void GpsStamp_RequiresCoordinates_TextStampRequiresText()
    {
        Assert.Throws<InvalidOperationException>(() => EditorOperations.Create(new EditorToolSettings { Tool = EditorTool.GpsStamp, Anchor = StampAnchor.TopLeft }, 100, 100));
        Assert.Throws<InvalidOperationException>(() => EditorOperations.Create(new EditorToolSettings { Tool = EditorTool.Text, Anchor = StampAnchor.TopLeft, Text = " " }, 100, 100));
        Assert.Throws<InvalidOperationException>(() => EditorOperations.Create(new EditorToolSettings { Tool = EditorTool.Text, Text = "x" }, 100, 100)); // no click position
    }

    [Fact]
    public void GpsStamp_DrawsInsideImage()
    {
        using var image = new Image<Rgba32>(300, 120, Color.White.ToPixel<Rgba32>());
        EditorOperations.Create(new EditorToolSettings { Tool = EditorTool.GpsStamp, Anchor = StampAnchor.BottomRight, Gps = new GeoCoordinate(1, 2), Color = Color.Black, StampPercent = 10 }, 300, 120)(image);
        Assert.Equal(Color.White.ToPixel<Rgba32>(), image[0, 0]);
    }
}
