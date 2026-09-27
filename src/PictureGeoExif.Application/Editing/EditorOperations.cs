using System.Globalization;
using PictureGeoExif.Core.Geo;
using PictureGeoExif.Metadata.Editing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PictureGeoExif.Application.Editing;

public enum EditorTool { Crop, Blur, Pixelate, Text, GpsStamp }

public enum StampAnchor { ClickPosition, TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>Everything a tool needs, in image pixels. Built by the UI from its controls; no UI types.</summary>
public sealed record EditorToolSettings
{
    public EditorTool Tool { get; init; }
    public Rectangle Selection { get; init; }
    /// <summary>Blur/pixelate strength in percent of the shorter selection edge.</summary>
    public double EffectPercent { get; init; } = 5;
    public string? Text { get; init; }
    /// <summary>Stamp text height in percent of the shorter image edge.</summary>
    public double StampPercent { get; init; } = 3;
    public StampAnchor Anchor { get; init; }
    public PointF? Click { get; init; }
    public Color Color { get; init; } = Color.White;
    public GeoCoordinate? Gps { get; init; }
}

/// <summary>Turns tool settings into a pixel operation for <see cref="EditorSession.ApplyAsync"/>. Same code for preview and apply.</summary>
public static class EditorOperations
{
    public static bool NeedsSelection(EditorTool tool) => tool is EditorTool.Crop or EditorTool.Blur or EditorTool.Pixelate;

    public static Action<Image<Rgba32>> Create(EditorToolSettings settings, int width, int height)
    {
        var region = settings.Selection;
        if (NeedsSelection(settings.Tool))
        {
            if (region.IsEmpty) throw new InvalidOperationException("Bitte einen nicht leeren Bereich auswählen.");
            float strength = (float)(Math.Min(region.Width, region.Height) * settings.EffectPercent / 100);
            return settings.Tool switch
            {
                EditorTool.Crop => image => image.Mutate(c => c.Crop(region)),
                EditorTool.Blur => image => EditorSession.Blur(image, region, strength),
                _ => image => EditorSession.Pixelate(image, region, Math.Max(1, (int)Math.Round(strength)))
            };
        }

        string text = settings.Text ?? "";
        if (settings.Tool == EditorTool.GpsStamp)
        {
            if (settings.Gps is not { } gps) throw new InvalidOperationException("Keine gültigen GPS-Koordinaten am Bild vorhanden.");
            text = string.Create(CultureInfo.InvariantCulture, $"Lat: {gps.Latitude:F6}\nLon: {gps.Longitude:F6}");
        }
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Bitte Text eingeben.");
        float size = (float)(Math.Min(width, height) * settings.StampPercent / 100);
        double margin = Math.Max(1, Math.Min(width, height) * 0.02);
        PointF origin = settings.Anchor switch
        {
            StampAnchor.TopLeft => new((float)margin, (float)margin),
            StampAnchor.TopRight => new((float)(width - margin), (float)margin),
            StampAnchor.BottomLeft => new((float)margin, (float)(height - margin)),
            StampAnchor.BottomRight => new((float)(width - margin), (float)(height - margin)),
            _ => settings.Click ?? throw new InvalidOperationException("Bitte Stempelposition wählen.")
        };
        bool right = settings.Anchor is StampAnchor.TopRight or StampAnchor.BottomRight;
        bool bottom = settings.Anchor is StampAnchor.BottomLeft or StampAnchor.BottomRight;
        var color = settings.Color;
        return image => EditorSession.Stamp(image, text, size, color, origin.X, origin.Y, right, bottom);
    }
}
