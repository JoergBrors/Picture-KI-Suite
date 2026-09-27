using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PictureGeoExif.Desktop.ViewModels;

namespace PictureGeoExif.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DataContextChanged += (_, _) => BuildMenu();
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    // ---------- Drag & drop from Finder / Explorer ----------

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];
        e.Handled = true;
        if (paths.Count > 0) await vm.LoadFilesAsync(paths);
    }

    // ---------- Map buttons ----------

    private void ZoomIn_Click(object? sender, RoutedEventArgs e) => MapView.ZoomBy(1);
    private void ZoomOut_Click(object? sender, RoutedEventArgs e) => MapView.ZoomBy(-1);
    private void Fit_Click(object? sender, RoutedEventArgs e) => MapView.FitToMarkers();

    // ---------- Menu (macOS menu bar, in-window menu elsewhere) ----------

    private void BuildMenu()
    {
        if (ViewModel is not { } vm) return;
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        NativeMenuItem Item(string header, System.Windows.Input.ICommand cmd, Key? key = null) => new(header)
        {
            Command = cmd,
            Gesture = key is { } k ? new KeyGesture(k, command) : null
        };
        NativeMenuItem Menu(string header, params NativeMenuItemBase[] items)
        {
            var menu = new NativeMenu();
            foreach (var item in items) menu.Add(item);
            return new NativeMenuItem(header) { Menu = menu };
        }
        var root = new NativeMenu
        {
            Menu("Datei",
                Item("Bilder öffnen …", vm.OpenFilesCommand, Key.O),
                Item("Ordner öffnen …", vm.OpenFolderCommand),
                Item("Ausgabeordner …", vm.ChangeOutputFolderCommand),
                new NativeMenuItemSeparator(),
                Item("Alle speichern", vm.SaveAllCommand, Key.S),
                Item("Liste leeren", vm.ClearFilesCommand)),
            Menu("Bearbeiten",
                Item("Rückgängig (Bild)", vm.UndoImageCommand, Key.Z),
                Item("Bild bearbeiten …", vm.EditImageCommand, Key.E),
                Item("GPS auf ausgewähltes Bild anwenden", vm.ApplyGpsCommand),
                Item("Referenzbild …", vm.UseReferenceImageCommand)),
            Menu("Apple Fotos",
                Item("Zugriff anfordern", vm.Photos.RequestAccessCommand),
                Item("Fotomediathek öffnen", vm.Photos.OpenLibraryCommand),
                Item("Original exportieren …", vm.ExportOriginalCommand),
                Item("In Dateien übernehmen", vm.ImportToFilesCommand)),
            Menu("Werkzeuge",
                Item("KI-Metadaten …", vm.OpenAiMetadataCommand),
                Item("Trassen an Wege anlegen", vm.RoadMatchCommand),
                Item("Routing-Einstellungen …", vm.OpenRoadSettingsCommand)),
            Menu("Help",
                Item("Diagnostics", vm.OpenDiagnosticsCommand),
                Item("Lizenzen", vm.OpenLicensesCommand))
        };
        NativeMenu.SetMenu(this, root);
    }
}
