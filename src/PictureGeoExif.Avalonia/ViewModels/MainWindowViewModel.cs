using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PictureGeoExif.Application;
using PictureGeoExif.Application.Maps;
using PictureGeoExif.Application.Photos;
using PictureGeoExif.Application.Sources;
using PictureGeoExif.Core.Geo;
using PictureGeoExif.Core.Imaging;
using PictureGeoExif.Core.IO;
using PictureGeoExif.Core.Photos;
using PictureGeoExif.Core.Sources;
using PictureGeoExif.Desktop.Services;
using PictureGeoExif.Metadata;

namespace PictureGeoExif.Desktop.ViewModels;

public enum SourceKind { Files, ApplePhotos }

/// <summary>
/// Main window: sources (files, Apple Photos), image list, preview, metadata, map, GPS and routes.
/// Originals are never modified: GPS and edits go to collision-free copies in the output folder; Photos assets are read-only.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private const int ListThumbnailSize = 256, PreviewSize = 1600;
    private readonly ImageService images;
    private readonly FileSystemImageSourceProvider files;
    private readonly PhotoOriginalCache originals;
    private readonly IPhotoLibraryService photos;
    private readonly IDialogService dialogs;
    private readonly IStorageService storage;
    private readonly IWindowService windows;
    private readonly ILogger<MainWindowViewModel> logger;
    private readonly SemaphoreSlim thumbnailGate = new(4);
    private CancellationTokenSource? selectionLoad;
    private readonly Dictionary<string, RouteRoadMatch> roadCache = [];
    private Dictionary<RoutePoint, ImageEntryViewModel> pointItems = [];
    private string? roadConsentServer; // consent is per server and session

    public MainWindowViewModel(AppSettings settings, ImageService images, FileSystemImageSourceProvider files, PhotoOriginalCache originals,
        IPhotoLibraryService photos, PhotosBrowserViewModel photosBrowser, MapViewModel map, IDialogService dialogs, IStorageService storage,
        IWindowService windows, ILogger<MainWindowViewModel> logger)
    {
        Settings = settings;
        this.images = images;
        this.files = files;
        this.originals = originals;
        this.photos = photos;
        Photos = photosBrowser;
        Map = map;
        this.dialogs = dialogs;
        this.storage = storage;
        this.windows = windows;
        this.logger = logger;
        routeGapMeters = Math.Clamp(settings.RouteMaxGapMeters, 10, 2000);
        branchMinMeters = Math.Clamp(settings.RouteBranchMinMeters, 0, 100);
        sortByRoute = settings.SortImagesByRoute;
        FileImages.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(FileCountText)); OnPropertyChanged(nameof(HasFiles)); SaveAllCommand.NotifyCanExecuteChanged(); ApplyGpsToAllCommand.NotifyCanExecuteChanged(); };
        Map.CoordinateSelected += (_, position) => SetCurrentCoordinate(position, "Neuer GPS-Punkt – mit „GPS auf ausgewähltes Bild anwenden“ übernehmen");
        Map.MarkerSelected += (_, id) => { if (id >= 0 && id < FileImages.Count) { SelectedSource = SourceKind.Files; SelectedImage = FileImages[id]; } };
    }

    public AppSettings Settings { get; }
    public PhotosBrowserViewModel Photos { get; }
    public MapViewModel Map { get; }
    public MetadataPanelViewModel Metadata { get; } = new();
    public ObservableCollection<ImageEntryViewModel> FileImages { get; } = [];

    public string FileCountText => $"{FileImages.Count} Bild(er) geladen";
    public bool HasFiles => FileImages.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleImages), nameof(IsFilesSource), nameof(IsPhotosSource))]
    private SourceKind selectedSource = SourceKind.Files;

    public bool IsFilesSource { get => SelectedSource == SourceKind.Files; set { if (value) SelectedSource = SourceKind.Files; } }
    public bool IsPhotosSource { get => SelectedSource == SourceKind.ApplePhotos; set { if (value) SelectedSource = SourceKind.ApplePhotos; } }
    public ObservableCollection<ImageEntryViewModel> VisibleImages => SelectedSource == SourceKind.Files ? FileImages : Photos.Assets;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyGpsCommand), nameof(SaveSelectedCommand), nameof(EditImageCommand), nameof(ExportOriginalCommand),
        nameof(RemoveImageCommand), nameof(UndoImageCommand), nameof(RetryLoadCommand), nameof(ImportToFilesCommand))]
    private ImageEntryViewModel? selectedImage;

    [ObservableProperty] private Bitmap? preview;
    [ObservableProperty] private string? previewStatus;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private double? progress;
    [ObservableProperty] private string status = "Bereit. Bilder per Dialog, Ordner oder Drag & Drop laden.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentGpsText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyGpsCommand), nameof(ApplyGpsToAllCommand))]
    private GeoCoordinate? currentCoordinate;

    public string CurrentGpsText => CurrentCoordinate is { } c ? $"Lat: {c.Latitude.ToString("F6", CultureInfo.InvariantCulture)}, Lng: {c.Longitude.ToString("F6", CultureInfo.InvariantCulture)}" : "Keine Koordinaten ausgewählt";

    [ObservableProperty] private double routeGapMeters;
    [ObservableProperty] private double branchMinMeters;
    [ObservableProperty] private bool sortByRoute;
    [ObservableProperty] private IReadOnlyList<Route> routes = [];

    public string OutputFolder => Settings.OutputFolder;
    public string RoadServerText
    {
        get
        {
            string host = Uri.TryCreate(Settings.RoadMatchUrl, UriKind.Absolute, out var uri) ? uri.Host : Settings.RoadMatchUrl;
            return $"Server: {host}{(Settings.RoadMatchUrl.TrimEnd('/') == RoadMatcher.DefaultServer ? " (öffentlicher Demo-Server, fair use)" : "")}";
        }
    }

    partial void OnRouteGapMetersChanged(double value) { Settings.RouteMaxGapMeters = value; RebuildRoutes(); }
    partial void OnBranchMinMetersChanged(double value) { Settings.RouteBranchMinMeters = value; RebuildRoutes(); }
    partial void OnSortByRouteChanged(bool value) { Settings.SortImagesByRoute = value; RebuildRoutes(); }

    public async Task InitializeAsync()
    {
        try { await Photos.RefreshStatusAsync(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { logger.LogError(ex, "Fotos-Status nicht lesbar."); }
    }

    public void OnShutdown()
    {
        selectionLoad?.Cancel();
        Photos.Cancel();
        Settings.Save();
        images.Dispose();
    }

    // ---------------- Sources ----------------

    partial void OnSelectedSourceChanged(SourceKind value)
    {
        SelectedImage = null;
        if (value == SourceKind.ApplePhotos && Photos.CanOpenLibrary && Photos.Albums.Count == 0) Photos.OpenLibraryCommand.Execute(null);
    }

    [RelayCommand]
    private async Task OpenFilesAsync()
    {
        var paths = await storage.PickImagesAsync();
        if (paths.Count > 0) await LoadFilesAsync(paths);
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var folder = await storage.PickFolderAsync("Ordner mit Bildern öffnen");
        if (folder != null) await LoadFilesAsync([folder]);
    }

    /// <summary>Adds files and folders (dialog, Finder drag &amp; drop). Unsupported files are skipped, duplicates ignored.</summary>
    public async Task LoadFilesAsync(IEnumerable<string> paths)
    {
        var expanded = paths.SelectMany(p => Directory.Exists(p) ? FileSystemImageSourceProvider.EnumerateFolder(p) : [p]).ToList();
        var added = files.Add(expanded);
        SelectedSource = SourceKind.Files;
        if (added.Count == 0)
        {
            Status = expanded.Count == 0 ? "Keine Dateien gefunden." : "Keine neuen unterstützten Bilder (JPG, PNG, TIFF, BMP, HEIC, DNG, RAW …).";
            return;
        }
        var entries = added.Select(item => new ImageEntryViewModel(item)).ToList();
        foreach (var entry in entries) FileImages.Add(entry);
        IsBusy = true;
        try
        {
            int done = 0;
            await Task.WhenAll(entries.Select(async entry =>
            {
                await thumbnailGate.WaitAsync();
                try
                {
                    string path = entry.Source.LocalPath!;
                    var (gps, bitmap) = await Task.Run(async () =>
                    {
                        var location = ImageService.ReadGps(path);
                        var bytes = await images.CreateThumbnailAsync(path, ListThumbnailSize);
                        return (location, bytes == null ? null : new Bitmap(new MemoryStream(bytes)));
                    });
                    entry.Location = gps;
                    entry.Thumbnail = bitmap;
                    if (bitmap == null) entry.LoadError = "Keine Vorschau für dieses Format";
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    entry.LoadError = "Bild konnte nicht gelesen werden";
                    logger.LogWarning(ex, "Bild konnte nicht geladen werden.");
                }
                finally
                {
                    thumbnailGate.Release();
                    Progress = 100.0 * Interlocked.Increment(ref done) / entries.Count;
                }
            }));
        }
        finally { IsBusy = false; Progress = null; }
        RebuildRoutes(fit: true);
        Status = $"{added.Count} Bild(er) geladen.";
        SelectedImage ??= entries[0];
    }

    [RelayCommand]
    private async Task ClearFilesAsync()
    {
        if (FileImages.Count == 0) return;
        if (!await dialogs.ConfirmAsync("Alle entfernen", $"Möchten Sie alle {FileImages.Count} Bilder aus der Liste entfernen?\n\n(Die Dateien werden nicht gelöscht)")) return;
        SelectedImage = null;
        FileImages.Clear();
        files.Clear();
        Map.SetCurrent(null);
        RebuildRoutes();
    }

    [RelayCommand(CanExecute = nameof(IsFileSelected))]
    private async Task RemoveImageAsync()
    {
        if (SelectedImage is not { IsPhotosAsset: false } entry) return;
        if (!await dialogs.ConfirmAsync("Bild entfernen", $"Möchten Sie '{entry.FileName}' aus der Liste entfernen?\n\n(Die Datei wird nicht gelöscht)")) return;
        SelectedImage = null;
        FileImages.Remove(entry);
        files.Remove(entry.Source);
        RebuildRoutes();
    }

    private bool IsFileSelected() => SelectedImage is { IsPhotosAsset: false };
    private bool IsPhotoSelected() => SelectedImage is { IsPhotosAsset: true };
    private bool HasSelection() => SelectedImage != null;

    // ---------------- Selection: preview + metadata (same pipeline for files and Photos) ----------------

    partial void OnSelectedImageChanged(ImageEntryViewModel? oldValue, ImageEntryViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsSelectedOnMap = false;
        selectionLoad?.Cancel();
        Preview = null;
        if (newValue == null)
        {
            Metadata.Clear("Kein Bild ausgewählt");
            PreviewStatus = null;
            Map.SetSelected(null, null);
            return;
        }
        newValue.IsSelectedOnMap = true;
        int index = FileImages.IndexOf(newValue);
        Map.SetSelected(index >= 0 && newValue.Location != null ? index : null, newValue.Location);
        if (newValue.Location is { } gps && !newValue.IsPhotosAsset) CurrentCoordinate = gps;
        Map.Info = Describe(newValue);
        selectionLoad = new CancellationTokenSource();
        _ = LoadSelectionAsync(newValue, selectionLoad.Token);
    }

    private async Task LoadSelectionAsync(ImageEntryViewModel entry, CancellationToken token)
    {
        try
        {
            entry.LoadError = null;
            string? path = entry.LocalPath;
            if (entry.IsPhotosAsset && path == null)
            {
                // Show the local thumbnail right away; the original may first come from iCloud.
                Preview = entry.Thumbnail;
                path = await MaterializeAsync(entry, token);
                if (path == null) return;
            }
            if (path == null || token.IsCancellationRequested) return;

            PreviewStatus = "Vorschau wird geladen …";
            var bytes = await images.CreateThumbnailAsync(path, PreviewSize, token);
            var bitmap = bytes == null ? null : await Task.Run(() => new Bitmap(new MemoryStream(bytes)), token);
            if (token.IsCancellationRequested) return;
            Preview = bitmap ?? entry.Thumbnail;
            PreviewStatus = bitmap == null ? $"Keine Vorschau für {ImageFormatCapabilities.DisplayName(entry.Source.OriginalFormat)} auf dieser Plattform verfügbar. Metadaten werden trotzdem gelesen." : null;

            var report = await Task.Run(() => MetadataInspector.Inspect(path, entry.FileName), token);
            if (token.IsCancellationRequested) return;
            Metadata.Show(report, SourceEntries(entry));
            // GPS from the original's EXIF wins; for Photos the asset location is the fallback.
            if (report.Gps is { } gps && entry.Location != gps)
            {
                entry.Location = gps;
                if (!entry.IsPhotosAsset) RebuildRoutes();
            }
            if (entry.Location is { } location) Map.SetSelected(FileImages.IndexOf(entry) is >= 0 and var i ? i : null, location);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogError(ex, "Auswahl konnte nicht geladen werden.");
            entry.LoadError = "Bild konnte nicht geladen werden.";
            PreviewStatus = "Asset konnte nicht geladen werden. Technische Details stehen im Log.";
            RetryLoadCommand.NotifyCanExecuteChanged();
        }
    }

    private static IEnumerable<MetadataEntry> SourceEntries(ImageEntryViewModel entry)
    {
        yield return new MetadataEntry(MetadataGroup.General, "Quelle", "Quelle", entry.SourceLabel);
        if (entry.IsPhotosAsset)
        {
            if (entry.Source.CreationDate is { } date) yield return new(MetadataGroup.General, "Apple Fotos", "Erstellungsdatum", date.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            yield return new(MetadataGroup.General, "Apple Fotos", "Pixel", $"{entry.Source.PixelWidth} × {entry.Source.PixelHeight}");
            yield return new(MetadataGroup.General, "Apple Fotos", "Favorit", entry.Source.IsFavorite ? "ja" : "nein");
            yield return new(MetadataGroup.General, "Apple Fotos", "Standort (Mediathek)", entry.Source.Location?.ToString() ?? "keiner");
            yield return new(MetadataGroup.General, "Apple Fotos", "Asset-Identifier", entry.Source.PhotoKitAssetId ?? "");
        }
    }

    /// <summary>Photos asset → original resource → local cached copy (read-only), with iCloud progress.</summary>
    private async Task<string?> MaterializeAsync(ImageEntryViewModel entry, CancellationToken token)
    {
        string id = entry.Source.PhotoKitAssetId!;
        PreviewStatus = originals.TryGetCached(id, entry.FileName, out _) ? "Original wird geladen …" : "Original wird geladen (ggf. aus iCloud) …";
        var progressReporter = new Progress<PhotoTransferProgress>(p =>
        {
            if (p.Fraction is { } f && f < 1 && p.FromCloud) PreviewStatus = $"Original wird aus iCloud geladen … {f:P0}";
        });
        var result = await originals.MaterializeAsync(id, entry.FileName, progressReporter, token);
        if (!result.Success)
        {
            if (result.Error == PhotoExportError.Cancelled) return null;
            entry.LoadError = result.UserMessage;
            PreviewStatus = $"Asset konnte nicht geladen werden.\nGrund: {result.UserMessage}";
            Metadata.Clear(entry.FileName, result.UserMessage, SourceEntries(entry));
            RetryLoadCommand.NotifyCanExecuteChanged();
            return null;
        }
        entry.MaterializedPath = result.DestinationPath;
        return result.DestinationPath;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RetryLoad()
    {
        if (SelectedImage is not { } entry) return;
        selectionLoad?.Cancel();
        selectionLoad = new CancellationTokenSource();
        _ = LoadSelectionAsync(entry, selectionLoad.Token);
    }

    // ---------------- Apple Photos: export ----------------

    [RelayCommand(CanExecute = nameof(IsPhotoSelected))]
    private async Task ExportOriginalAsync()
    {
        if (SelectedImage is not { IsPhotosAsset: true } entry) return;
        var target = await storage.PickSaveFileAsync("Original exportieren", AtomicFile.SafeFileName(entry.FileName, "original"), Settings.OutputFolder);
        if (target == null) return;
        // The save panel already asked about overwriting; never overwrite silently anyway.
        if (File.Exists(target)) target = AtomicFile.FreePath(target);
        var result = await ExportAsync(entry, target);
        if (result.Success) Status = $"Original exportiert: {result.DestinationPath}";
    }

    /// <summary>Exports the Photos original into the output folder and adds the new file to the file list (then it can be edited, geotagged, analysed).</summary>
    [RelayCommand(CanExecute = nameof(IsPhotoSelected))]
    private async Task ImportToFilesAsync()
    {
        if (SelectedImage is not { IsPhotosAsset: true } entry) return;
        string target = AtomicFile.ExportPath(Path.Combine(Settings.OutputFolder, "Apple Fotos"), AtomicFile.SafeFileName(entry.FileName, "original"));
        var result = await ExportAsync(entry, target);
        if (result.Success && result.DestinationPath != null) await LoadFilesAsync([result.DestinationPath]);
    }

    private async Task<PhotoExportResult> ExportAsync(ImageEntryViewModel entry, string target)
    {
        IsBusy = true;
        Progress = 0;
        Status = "Original wird exportiert …";
        try
        {
            var reporter = new Progress<PhotoTransferProgress>(p =>
            {
                if (p.Fraction is { } f) { Progress = f * 100; if (p.FromCloud && f < 1) Status = "Original wird aus iCloud geladen …"; }
            });
            var result = await photos.ExportOriginalAsync(entry.Source.PhotoKitAssetId!, target, reporter);
            if (!result.Success)
            {
                logger.LogWarning("Export fehlgeschlagen: {Error}", result.Error);
                Status = result.UserMessage;
                await dialogs.ShowMessageAsync("Original exportieren", $"Asset konnte nicht exportiert werden.\nGrund: {result.UserMessage}");
            }
            return result;
        }
        finally { IsBusy = false; Progress = null; }
    }

    // ---------------- GPS ----------------

    private void SetCurrentCoordinate(GeoCoordinate position, string info)
    {
        CurrentCoordinate = position;
        Map.SetCurrent(position);
        Map.Info = $"{info}: {position}";
    }

    [RelayCommand]
    private async Task UseReferenceImageAsync()
    {
        var picked = await storage.PickImagesAsync(false, "Referenzbild mit GPS-Daten auswählen");
        if (picked.Count == 0) return;
        var gps = await Task.Run(() => ImageService.ReadGps(picked[0]));
        if (gps is { } position)
        {
            SetCurrentCoordinate(position, "GPS aus Referenzbild");
            Map.CenterOn(position);
            Status = "GPS-Koordinaten aus Referenzbild geladen.";
        }
        else await dialogs.ShowMessageAsync("Referenzbild", "Das Referenzbild enthält keine gültigen GPS-Daten.");
    }

    private bool CanApplyGps() => SelectedImage is { IsPhotosAsset: false } && CurrentCoordinate != null;

    /// <summary>Writes the current coordinate into a new copy of the selected file (original unchanged).</summary>
    [RelayCommand(CanExecute = nameof(CanApplyGps))]
    private async Task ApplyGpsAsync()
    {
        if (SelectedImage is not { IsPhotosAsset: false } entry || CurrentCoordinate is not { } gps) return;
        if (!ImageFormatCapabilities.CanWriteGps(entry.Source.OriginalFormat))
        {
            await dialogs.ShowMessageAsync("GPS schreiben", $"{ImageFormatCapabilities.DisplayName(entry.Source.OriginalFormat)} kann keine GPS-Metadaten speichern. Unterstützt: JPEG, PNG, TIFF.");
            return;
        }
        if (entry.Location is { } existing && !await dialogs.ConfirmAsync("GPS-Daten ersetzen?",
                $"Das Bild '{entry.FileName}' hat bereits GPS-Koordinaten:\n\nAktuell: {existing}\nNeu: {gps}\n\nMöchten Sie die vorhandenen Koordinaten ersetzen?"))
            return;
        await SaveCopiesAsync([entry], _ => gps, "Bild mit GPS-Koordinaten gespeichert");
    }

    private bool CanApplyGpsToAll() => FileImages.Count > 0 && CurrentCoordinate != null;

    [RelayCommand(CanExecute = nameof(CanApplyGpsToAll))]
    private async Task ApplyGpsToAllAsync()
    {
        if (CurrentCoordinate is not { } gps) return;
        if (!await dialogs.ConfirmAsync("GPS auf alle anwenden", $"Koordinate {gps} in Kopien aller {FileImages.Count} Bilder schreiben?\nDie Originale bleiben unverändert.")) return;
        await SaveCopiesAsync(FileImages.ToList(), _ => gps, "Bilder mit GPS gespeichert");
    }

    [RelayCommand(CanExecute = nameof(HasFiles))]
    private async Task SaveAllAsync() =>
        // Preserve each image's own coordinates.
        await SaveCopiesAsync(FileImages.ToList(), entry => entry.Location, "Bilder gespeichert");

    [RelayCommand(CanExecute = nameof(IsFileSelected))]
    private async Task SaveSelectedAsync()
    {
        if (SelectedImage is not { IsPhotosAsset: false } entry) return;
        await SaveCopiesAsync([entry], e => e.Location ?? CurrentCoordinate, "Bild gespeichert");
    }

    /// <summary>Copies files to the output folder (optionally with GPS) in the background; the list then shows the new copies. Undo per image.</summary>
    private async Task SaveCopiesAsync(IReadOnlyList<ImageEntryViewModel> entries, Func<ImageEntryViewModel, GeoCoordinate?> coordinate, string done)
    {
        IsBusy = true;
        int saved = 0, failed = 0;
        string? lastPath = null, lastError = null;
        try
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var gps = coordinate(entry);
                if (gps != null && !ImageFormatCapabilities.CanWriteGps(entry.Source.OriginalFormat)) gps = null;
                string source = entry.Source.LocalPath!;
                try
                {
                    string newPath = await Task.Run(() => images.SaveSingleImage(source, Settings.OutputFolder, gps?.Latitude, gps?.Longitude));
                    images.InvalidateThumbnailCache(source);
                    entry.PushHistory();
                    entry.Source = files.Replace(entry.Source, newPath);
                    if (gps != null) entry.Location = gps;
                    var thumb = await images.CreateThumbnailAsync(newPath, ListThumbnailSize);
                    if (thumb != null) entry.Thumbnail = new Bitmap(new MemoryStream(thumb));
                    lastPath = newPath;
                    saved++;
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
                {
                    failed++;
                    lastError = ex.Message;
                    logger.LogWarning(ex, "Speichern fehlgeschlagen.");
                }
                Progress = 100.0 * (i + 1) / entries.Count;
            }
        }
        finally { IsBusy = false; Progress = null; }
        RebuildRoutes();
        UndoImageCommand.NotifyCanExecuteChanged();
        Status = entries.Count == 1 && saved == 1 ? $"{done}: {lastPath}" : $"{done}: {saved} erfolgreich, {failed} Fehler.";
        if (failed > 0) await dialogs.ShowMessageAsync("Speichern", $"{saved} Bild(er) gespeichert, {failed} Fehler.\nLetzter Fehler: {lastError}");
    }

    private bool CanUndo() => SelectedImage is { CanUndo: true };

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoImageAsync()
    {
        if (SelectedImage is not { CanUndo: true } entry) return;
        string? replaced = entry.Undo();
        if (replaced != null) images.InvalidateThumbnailCache(replaced);
        if (entry.Source.LocalPath is { } path && File.Exists(path))
        {
            var bytes = await images.CreateThumbnailAsync(path, ListThumbnailSize);
            entry.Thumbnail = bytes == null ? null : new Bitmap(new MemoryStream(bytes));
        }
        RebuildRoutes();
        RetryLoad();
        Status = $"Rückgängig: {entry.FileName} zeigt wieder den vorherigen Stand. Die zuvor gespeicherte Kopie bleibt erhalten: {replaced}";
        UndoImageCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task ChangeOutputFolderAsync()
    {
        var folder = await storage.PickFolderAsync("Ausgabeordner für bearbeitete Bilder wählen", Settings.OutputFolder);
        if (folder == null) return;
        Settings.OutputFolder = folder;
        Settings.Save();
        OnPropertyChanged(nameof(OutputFolder));
        Status = "Speicherort geändert: " + folder;
    }

    // ---------------- Editor, AI, windows ----------------

    [RelayCommand(CanExecute = nameof(IsFileSelected))]
    private async Task EditImageAsync()
    {
        if (SelectedImage is not { IsPhotosAsset: false } entry || entry.Source.LocalPath is not { } path) return;
        if (!entry.CanEdit)
        {
            await dialogs.ShowMessageAsync("Bildeditor", $"{ImageFormatCapabilities.DisplayName(entry.Source.OriginalFormat)} kann im Editor nicht geöffnet werden. Bitte zuerst als JPEG/PNG exportieren.");
            return;
        }
        var result = await windows.ShowEditorAsync(path, entry.Location);
        if (result == null) return;
        try
        {
            string newPath = await Task.Run(() =>
            {
                string saved = images.SaveEditedImage(result.Value.Bytes, Path.ChangeExtension(entry.FileName, result.Value.Extension), Settings.OutputFolder);
                if (entry.Location is { } gps && ImageFormatCapabilities.CanWriteGps(ImageFormatDetector.FromExtension(result.Value.Extension)))
                    images.WriteGpsToImage(saved, gps.Latitude, gps.Longitude);
                return saved;
            });
            entry.PushHistory();
            entry.Source = files.Replace(entry.Source, newPath);
            var bytes = await images.CreateThumbnailAsync(newPath, ListThumbnailSize);
            entry.Thumbnail = bytes == null ? null : new Bitmap(new MemoryStream(bytes));
            RetryLoad();
            Status = "Bearbeitetes Bild gespeichert: " + newPath;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Bearbeitetes Bild konnte nicht gespeichert werden.");
            await dialogs.ShowMessageAsync("Bildeditor", "Fehler beim Speichern: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task OpenAiMetadataAsync()
    {
        var paths = FileImages.Select(e => e.Source.LocalPath).OfType<string>().ToList();
        if (paths.Count == 0)
        {
            await dialogs.ShowMessageAsync("KI-Metadaten", "Bitte zuerst Bilder als Dateien laden. Fotos aus Apple Fotos vorher mit „In Dateien übernehmen“ exportieren – sie laufen dann durch dieselbe Datenschutzprüfung.");
            return;
        }
        windows.ShowAiMetadata(paths);
    }

    [RelayCommand] private void OpenDiagnostics() => windows.ShowDiagnostics();
    [RelayCommand] private void OpenLicenses() => windows.ShowLicenses();

    [RelayCommand]
    private async Task OpenRoadSettingsAsync()
    {
        if (await windows.ShowRoadSettingsAsync())
        {
            OnPropertyChanged(nameof(RoadServerText));
            ApplyRoadLayer(); // settings are part of the cache key: old results disappear until matched again
        }
    }

    [RelayCommand]
    private async Task OpenMapAttributionAsync()
    {
        if (Map.AttributionUrl is { Scheme: "https" } uri) await storage.OpenUrlAsync(uri);
    }

    // ---------------- Routes and map ----------------

    private string Describe(ImageEntryViewModel item) =>
        item.FileName + (item.RouteNumber > 0 ? " · " + item.RouteInfo : "") +
        (item.Location is { } gps ? " · " + gps : " · keine GPS-Daten") +
        (item.RoadDistance is { } m ? string.Create(CultureInfo.InvariantCulture, $" · {m:F0} m zum Weg") : "");

    /// <summary>
    /// Rebuilds the virtual routes (trunk + branches) from all GPS positions, numbers the images along them and optionally
    /// reorders the list. Map markers address images by their list index, so this runs after every GPS or list change.
    /// </summary>
    public void RebuildRoutes(bool fit = false)
    {
        var snapshot = FileImages.ToList();
        var points = snapshot.Select((img, i) => (img, i)).Where(x => x.img.Location != null)
            .Select(x => new RoutePoint(x.i, x.img.Location!.Value.Latitude, x.img.Location!.Value.Longitude)).ToList();
        Routes = RouteBuilder.Build(points, RouteGapMeters, BranchMinMeters);
        pointItems = points.ToDictionary(p => p, p => snapshot[p.Id]);

        var position = new Dictionary<ImageEntryViewModel, (int Route, int Index, bool Branch)>();
        foreach (var route in Routes)
            for (int k = 0; k < route.Points.Count; k++)
                position[pointItems[route.Points[k]]] = (route.Number, k + 1, route.IsBranchPoint(route.Points[k]));
        foreach (var img in FileImages)
        {
            var (route, index, branch) = position.TryGetValue(img, out var p) ? p : (0, 0, false);
            img.SetRoute(route, index, branch);
        }

        if (SortByRoute)
        {
            // Stable: images without GPS keep their relative order at the end.
            var ordered = FileImages.OrderBy(i => i.RouteNumber == 0 ? int.MaxValue : i.RouteNumber).ThenBy(i => i.RouteIndex).ToList();
            for (int target = 0; target < ordered.Count; target++)
            {
                int current = FileImages.IndexOf(ordered[target]);
                if (current != target) FileImages.Move(current, target);
            }
        }

        Map.SetRoutes(RouteLayers.Routes(Routes));
        Map.SetMarkers(FileImages.Select((img, index) => (img, index)).Where(x => x.img.Location != null)
            .Select(x => new MapMarker(x.index, x.img.Location!.Value, x.img.FileName)).ToList(), fit);
        if (SelectedImage is { } selected && FileImages.IndexOf(selected) is var i and >= 0) Map.SetSelected(selected.Location != null ? i : null, selected.Location);
        ApplyRoadLayer();
        if (SelectedImage == null)
            Map.Info = Routes.Count > 0 ? $"{Routes.Count} Trasse(n) · {Routes.Sum(r => r.Points.Count)} Bild(er) mit GPS" : "Kein Bild ausgewählt";
    }

    /// <summary>Shows cached road matches for the current routes; never contacts the server.</summary>
    private void ApplyRoadLayer()
    {
        var (segments, distances) = RouteLayers.Road(Routes, roadCache, Settings);
        foreach (var img in FileImages) img.RoadDistance = null;
        foreach (var (point, distance) in distances)
            if (pointItems.TryGetValue(point, out var item)) item.RoadDistance = distance;
        Map.SetRoadSegments(segments);
    }

    [RelayCommand]
    private async Task RoadMatchAsync()
    {
        var pending = Routes.Where(r => r.Points.Count >= 2 && !roadCache.ContainsKey(RouteLayers.RoadKey(r, Settings))).ToList();
        if (Routes.All(r => r.Points.Count < 2)) { Map.Status = "Keine Trasse mit mindestens zwei GPS-Punkten vorhanden."; return; }
        if (pending.Count == 0) { Map.Status = "Alle Trassen sind bereits an Wege angelegt."; ApplyRoadLayer(); return; }
        if (!RoadMatcher.IsAllowedServer(Settings.RoadMatchUrl, out var server)) { Map.Status = "Routing-Server ungültig – bitte in den Einstellungen ändern."; return; }

        int requests = pending.Sum(r => (r.Trunk.Count >= 2 ? 1 : 0) + r.Branches.Count);
        if (roadConsentServer != server.AbsoluteUri)
        {
            bool demo = Settings.RoadMatchUrl.TrimEnd('/') == RoadMatcher.DefaultServer;
            bool ok = await dialogs.ConfirmAsync("Trassen an Wege anlegen",
                $"Die Koordinaten von {pending.Sum(r => r.Points.Count)} Fotopunkten ({pending.Count} Trasse(n), {pending.Sum(r => r.Branches.Count)} Abzweig(e), mind. {requests} Anfrage(n)) werden an\n{server.Host}\ngesendet, um den Verlauf an Straßen und Wege anzulegen.\n\n" +
                (demo ? "Das ist der öffentliche Demo-Server des FOSSGIS e.V.: nur faire, gelegentliche Nutzung, höchstens 1 Anfrage pro Sekunde, keine Verfügbarkeitszusage. Für den Firmeneinsatz einen eigenen Server eintragen.\n\n" : "") +
                "Die GPS-Daten der Fotos werden nicht verändert. Fortfahren?");
            if (!ok) return;
            roadConsentServer = server.AbsoluteUri;
        }

        var matcher = new RoadMatcher(RoadMatcher.SharedClient, server, Settings.RoadMatchProfile, Settings.RoadMatchMaxDeviationMeters);
        var notes = new List<string>();
        IsBusy = true;
        try
        {
            foreach (var route in pending)
            {
                string key = RouteLayers.RoadKey(route, Settings);
                Map.Status = $"Trasse {route.Number} wird an Wege angelegt … ({pending.IndexOf(route) + 1}/{pending.Count})";
                var trunk = await matcher.MatchAsync(route.Trunk, CancellationToken.None);
                if (trunk.Note != null) notes.Add($"Trasse {route.Number}: {trunk.Note}");
                var branches = new List<RoadMatch>();
                foreach (var branch in route.Branches)
                {
                    Map.Status = $"Trasse {route.Number}: Abzweig {branches.Count + 1}/{route.Branches.Count} wird angelegt …";
                    branches.Add(await matcher.MatchAsync(RouteLayers.BranchRequest(branch), CancellationToken.None));
                }
                roadCache[key] = new RouteRoadMatch(trunk, branches);
                ApplyRoadLayer();
            }
            Map.Status = notes.Count == 0 ? $"{pending.Count} Trasse(n) an Wege angelegt." : string.Join(" ", notes);
        }
        catch (RoadMatchException ex)
        {
            // Stop on transport/server errors instead of retrying: protects the public server.
            Map.Status = "Anlegen an Wege abgebrochen: " + ex.Message;
        }
        finally { IsBusy = false; }
    }
}
