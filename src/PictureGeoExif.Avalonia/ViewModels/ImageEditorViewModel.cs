using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureGeoExif.Application.Editing;
using PictureGeoExif.Core.Geo;
using PictureGeoExif.Desktop.Services;
using PictureGeoExif.Metadata.Editing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using PixelRect = SixLabors.ImageSharp.Rectangle;
using SharpColor = SixLabors.ImageSharp.Color;

namespace PictureGeoExif.Desktop.ViewModels;

public sealed record NamedColor(string Name, SharpColor Color)
{
    public override string ToString() => Name;
}

public sealed record ExportFormat(string Name, string Extension)
{
    public override string ToString() => Name;
}

/// <summary>
/// Image editor: crop, blur, pixelate, text and GPS stamp with lossless history (EditorSession). All pixel work is done by
/// the metadata/application layer in image pixels; the view only renders and reports pointer positions in image pixels.
/// </summary>
public sealed partial class ImageEditorViewModel(string path, GeoCoordinate? gps, IDialogService dialogs) : ObservableObject, IDisposable
{
    private EditorSession? session;
    private CancellationTokenSource? operation;
    private (double X, double Y) start;

    public string OriginalPath => path;
    public string Info => $"{Path.GetFileName(path)}\n{(session != null ? $"{session.Width} × {session.Height} Pixel" : "")}";
    public string GpsInfo => gps is { } g ? "GPS: " + g : "Keine GPS-Daten";
    public (byte[] Bytes, string Extension)? Result { get; private set; }

    public static IReadOnlyList<NamedColor> Colors { get; } =
    [
        new("Weiß", SharpColor.White), new("Schwarz", SharpColor.Black), new("Gelb", SharpColor.Yellow),
        new("Rot", SharpColor.Red), new("Blau", SharpColor.DodgerBlue), new("Grün", SharpColor.LimeGreen)
    ];

    public static IReadOnlyList<string> Anchors { get; } = ["Klickposition", "Oben links", "Oben rechts", "Unten links", "Unten rechts"];

    public static IReadOnlyList<ExportFormat> Formats { get; } =
        [new("PNG (verlustfrei)", ".png"), new("JPEG", ".jpg"), new("TIFF", ".tif"), new("BMP", ".bmp")];

    [ObservableProperty] private Bitmap? display;
    [ObservableProperty] private int displayWidth;
    [ObservableProperty] private int displayHeight;
    [ObservableProperty] private double zoom = 1;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string status = "Bild wird geladen …";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCrop), nameof(IsBlur), nameof(IsPixelate), nameof(IsText), nameof(IsGps), nameof(NeedsSelection))]
    private EditorTool tool = EditorTool.Crop;

    public bool IsCrop { get => Tool == EditorTool.Crop; set { if (value) Tool = EditorTool.Crop; } }
    public bool IsBlur { get => Tool == EditorTool.Blur; set { if (value) Tool = EditorTool.Blur; } }
    public bool IsPixelate { get => Tool == EditorTool.Pixelate; set { if (value) Tool = EditorTool.Pixelate; } }
    public bool IsText { get => Tool == EditorTool.Text; set { if (value) Tool = EditorTool.Text; } }
    public bool IsGps { get => Tool == EditorTool.GpsStamp; set { if (value) Tool = EditorTool.GpsStamp; } }
    public bool NeedsSelection => EditorOperations.NeedsSelection(Tool);

    [ObservableProperty] private double effectPercent = 5;
    [ObservableProperty] private double stampPercent = 3;
    [ObservableProperty] private string text = "";
    [ObservableProperty] private int anchorIndex;
    [ObservableProperty] private NamedColor stampColor = Colors[0];
    [ObservableProperty] private ExportFormat exportFormat = Formats[0];
    [ObservableProperty] private double jpegQuality = 95;

    // Selection and click in image pixels (exclusive right/bottom edges).
    [ObservableProperty] private PixelRect selection = PixelRect.Empty;
    [ObservableProperty] private bool hasSelection;
    [ObservableProperty] private double clickX;
    [ObservableProperty] private double clickY;
    [ObservableProperty] private bool hasClick;
    [ObservableProperty] private bool isComparing;
    [ObservableProperty] private bool isPreviewing;

    public bool HasChanges => session?.HasChanges == true;
    public bool CanUndo => session?.CanUndo == true && !IsBusy;
    public bool CanRedo => session?.CanRedo == true && !IsBusy;

    public async Task LoadAsync()
    {
        await RunAsync(async token =>
        {
            session = await EditorSession.OpenAsync(path, token);
            ExportFormat = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => Formats[1], ".tif" or ".tiff" => Formats[2], ".bmp" => Formats[3], _ => Formats[0]
            };
            await ShowCurrentAsync();
        });
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        operation = new CancellationTokenSource();
        try { await action(operation.Token); }
        catch (OperationCanceledException) { Status = "Vorgang abgebrochen; letzter vollständiger Zustand bleibt erhalten."; }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or InvalidDataException or IOException or NotSupportedException or UnknownImageFormatException)
        {
            Status = ex.Message;
            await dialogs.ShowMessageAsync("Bildeditor", ex.Message);
        }
        finally
        {
            IsBusy = false;
            operation.Dispose();
            operation = null;
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        }
    }

    private static async Task<(Bitmap Bitmap, int Width, int Height)> ToBitmapAsync(Func<Image<Rgba32>> load, CancellationToken token) =>
        await Task.Run(() =>
        {
            using var image = load();
            token.ThrowIfCancellationRequested();
            using var stream = new MemoryStream();
            image.SaveAsPng(stream);
            stream.Position = 0;
            return (new Bitmap(stream), image.Width, image.Height);
        }, token);

    private async Task ShowCurrentAsync()
    {
        if (session == null) return;
        var (bitmap, width, height) = await ToBitmapAsync(() => SixLabors.ImageSharp.Image.Load<Rgba32>(session.CurrentPath), CancellationToken.None);
        Display = bitmap; DisplayWidth = width; DisplayHeight = height;
        IsComparing = IsPreviewing = false;
        OnPropertyChanged(nameof(Info));
        Status = "Bereit. Vorschau und Bearbeitung verwenden dieselben Bildpixel.";
    }

    // ---------- Pointer input (image pixels) ----------

    public void PointerDown(double x, double y)
    {
        if (IsBusy || session == null || IsComparing || IsPreviewing) return;
        if (x < 0 || y < 0 || x > session.Width || y > session.Height) return;
        start = (x, y);
        ClickX = x; ClickY = y; HasClick = !NeedsSelection;
        if (NeedsSelection) UpdateSelection(x, y);
    }

    public void PointerMove(double x, double y)
    {
        if (NeedsSelection && session != null && !IsComparing && !IsPreviewing) UpdateSelection(x, y);
    }

    private void UpdateSelection(double x, double y)
    {
        if (session == null) return;
        Selection = PixelGeometry.Selection(start.X, start.Y, x, y, session.Width, session.Height);
        HasSelection = !Selection.IsEmpty;
        if (HasSelection) Status = $"Auswahl: {Selection.X}, {Selection.Y} — {Selection.Width} × {Selection.Height} Pixel";
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        if (IsBusy) return;
        Selection = PixelRect.Empty; HasSelection = false; HasClick = false;
        await ShowCurrentAsync();
    }

    partial void OnToolChanged(EditorTool value) { Selection = PixelRect.Empty; HasSelection = false; HasClick = false; }

    private Action<Image<Rgba32>> Operation()
    {
        if (session == null) throw new InvalidOperationException("Kein Bild geladen.");
        return EditorOperations.Create(new EditorToolSettings
        {
            Tool = Tool,
            Selection = Selection,
            EffectPercent = EffectPercent,
            Text = Text,
            StampPercent = StampPercent,
            Anchor = (StampAnchor)AnchorIndex,
            Click = HasClick ? new PointF((float)ClickX, (float)ClickY) : null,
            Color = StampColor.Color,
            Gps = gps
        }, session.Width, session.Height);
    }

    [RelayCommand]
    private Task ApplyAsync() => RunAsync(async token =>
    {
        var action = Operation();
        await session!.ApplyAsync(action, token);
        Selection = PixelRect.Empty; HasSelection = false; HasClick = false;
        await ShowCurrentAsync();
    });

    [RelayCommand]
    private Task PreviewAsync() => RunAsync(async token =>
    {
        var action = Operation();
        string current = session!.CurrentPath;
        var (bitmap, width, height) = await ToBitmapAsync(() =>
        {
            var image = SixLabors.ImageSharp.Image.Load<Rgba32>(current);
            action(image);
            return image;
        }, token);
        Display = bitmap; DisplayWidth = width; DisplayHeight = height;
        IsPreviewing = true;
        Status = "Vorschau — Anwenden übernimmt, Löschen kehrt zum Arbeitsbild zurück.";
    });

    [RelayCommand] private Task RotateLeftAsync() => RotateAsync(RotateMode.Rotate270);
    [RelayCommand] private Task RotateRightAsync() => RotateAsync(RotateMode.Rotate90);

    private Task RotateAsync(RotateMode mode) => RunAsync(async token =>
    {
        await session!.ApplyAsync(i => i.Mutate(c => c.Rotate(mode)), token);
        Selection = PixelRect.Empty; HasSelection = false;
        await ShowCurrentAsync();
    });

    [RelayCommand]
    private async Task UndoAsync()
    {
        if (IsBusy || session?.CanUndo != true) return;
        session.Undo();
        await ShowCurrentAsync();
        OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(CanRedo));
    }

    [RelayCommand]
    private async Task RedoAsync()
    {
        if (IsBusy || session?.CanRedo != true) return;
        session.Redo();
        await ShowCurrentAsync();
        OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(CanRedo));
    }

    [RelayCommand]
    private async Task CompareAsync()
    {
        if (IsBusy || session == null) return;
        if (IsComparing) { await ShowCurrentAsync(); return; }
        await RunAsync(async token =>
        {
            var (bitmap, width, height) = await ToBitmapAsync(() =>
            {
                var image = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
                image.Mutate(c => c.AutoOrient());
                return image;
            }, token);
            Display = bitmap; DisplayWidth = width; DisplayHeight = height;
            IsComparing = true;
            Status = "Originalansicht — erneut klicken für bearbeitetes Bild.";
        });
    }

    [RelayCommand] private void CancelOperation() => operation?.Cancel();

    /// <summary>Encodes the current state; the main window saves it as a new file in the output folder.</summary>
    public async Task<bool> ExportAsync()
    {
        if (session == null || IsBusy) return false;
        await RunAsync(async token =>
        {
            var bytes = await session.ExportAsync(ExportFormat.Extension, (int)JpegQuality, token);
            Result = (bytes, ExportFormat.Extension);
        });
        return Result != null;
    }

    public void Dispose()
    {
        operation?.Cancel();
        session?.Dispose();
        session = null;
    }
}
