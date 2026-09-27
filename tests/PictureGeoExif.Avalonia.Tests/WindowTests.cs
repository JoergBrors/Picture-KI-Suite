using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using PictureGeoExif.Application;
using PictureGeoExif.Application.Ai;
using PictureGeoExif.Core.Geo;
using PictureGeoExif.Core.Security;
using PictureGeoExif.Desktop;
using PictureGeoExif.Desktop.Services;
using PictureGeoExif.Desktop.ViewModels;
using PictureGeoExif.Desktop.Views;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace PictureGeoExif.Avalonia.Tests;

/// <summary>Constructs every window headless with real view models; catches XAML, binding and init-order errors.</summary>
public sealed class WindowTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pge-ui-" + Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider services;

    public WindowTests()
    {
        var paths = new AppPaths
        {
            SettingsFolder = Path.Combine(root, "settings"), DataFolder = Path.Combine(root, "data"),
            CacheFolder = Path.Combine(root, "cache"), LogFolder = Path.Combine(root, "logs")
        };
        // Tests must never touch real user settings.
        AppPaths.Current = paths;
        services = Bootstrap.BuildServices(paths, fileLogging: false);
        services.GetRequiredService<AppSettings>().OutputFolder = Path.Combine(root, "out");
    }

    public void Dispose()
    {
        services.Dispose();
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    private string Jpeg(string name, double? lat = null, double? lon = null)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, name);
        using var image = new Image<Rgba32>(64, 48, new Rgba32(20, 120, 200));
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.DateTimeOriginal, "2024:01:15 10:20:30");
        image.SaveAsJpeg(path);
        if (lat is { } a && lon is { } o) using (var images = new PictureGeoExif.Metadata.ImageService()) images.WriteGpsToImage(path, a, o);
        return path;
    }

    private MainWindow ShowMain(out MainWindowViewModel vm)
    {
        vm = services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = vm };
        services.GetRequiredService<UiContext>().Attach(window);
        window.Show();
        return window;
    }

    [AvaloniaFact]
    public void MainWindow_Shows_WithMenuAndMap()
    {
        var window = ShowMain(out var vm);
        Assert.NotNull(NativeMenu.GetMenu(window));
        Assert.Equal(SourceKind.Files, vm.SelectedSource);
        Assert.Contains("Nicht verfügbar", vm.Photos.StatusText, StringComparison.Ordinal); // no PhotoKit on the CI/Linux host
        window.Close();
    }

    [AvaloniaFact]
    public async Task LoadingFiles_ReadsGps_BuildsRoutes_AndShowsMetadata()
    {
        var window = ShowMain(out var vm);
        string a = Jpeg("a.jpg", 52.5200, 13.4050), b = Jpeg("b.jpg", 52.5205, 13.4052), c = Jpeg("c.jpg");
        await vm.LoadFilesAsync([a, b, c, Path.Combine(root, "missing.jpg")]);
        Assert.Equal(3, vm.FileImages.Count);
        Assert.Equal(2, vm.FileImages.Count(i => i.HasGps));
        Assert.Single(vm.Routes);
        Assert.Equal(2, vm.Map.Markers.Count);

        vm.SelectedImage = vm.FileImages.First(i => i.FileName == "c.jpg");
        for (int i = 0; i < 100 && vm.Metadata.Report == null; i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
        Assert.NotNull(vm.Metadata.Report);
        Assert.Contains(vm.Metadata.General, e => e.Name == "Aufnahmezeit");
        window.Close();
    }

    [AvaloniaFact]
    public async Task ApplyGps_WritesCopy_KeepsOriginal_AndUndoRestores()
    {
        var window = ShowMain(out var vm);
        string original = Jpeg("x.jpg");
        byte[] before = File.ReadAllBytes(original);
        await vm.LoadFilesAsync([original]);
        vm.SelectedImage = vm.FileImages[0];
        vm.Map.OnClicked(new GeoCoordinate(48.137, 11.575));
        Assert.True(vm.ApplyGpsCommand.CanExecute(null));
        await vm.ApplyGpsCommand.ExecuteAsync(null);

        var entry = vm.FileImages[0];
        Assert.NotEqual(original, entry.Source.LocalPath);
        Assert.Equal(48.137, entry.Location!.Value.Latitude, 5);
        Assert.Equal(before, File.ReadAllBytes(original));
        Assert.True(entry.CanUndo);
        await vm.UndoImageCommand.ExecuteAsync(null);
        Assert.Equal(original, entry.Source.LocalPath);
        window.Close();
    }

    [AvaloniaFact]
    public async Task DiagnosticsWindow_ListsRequiredItems()
    {
        ShowMain(out _);
        var vm = new DiagnosticsViewModel(services.GetRequiredService<PictureGeoExif.Application.Diagnostics.DiagnosticsService>(),
            services.GetRequiredService<IStorageService>(), services.GetRequiredService<PictureGeoExif.Application.Photos.PhotoOriginalCache>(), services.GetRequiredService<AppPaths>());
        var window = new DiagnosticsWindow { DataContext = vm };
        window.Show();
        await vm.LoadAsync();
        foreach (var name in new[] { "Application Version", ".NET Runtime", "Operating System", "Architecture", "App Bundle Path",
                     "PhotoKit available", "PhotoKit authorization", "Image decoder capabilities", "EXIF capability", "XMP capability (read)" })
            Assert.Contains(vm.Items, i => i.Name == name);
        window.Close();
    }

    [AvaloniaFact]
    public void AiMetadataWindow_LoadsDefaultTemplate()
    {
        ShowMain(out _);
        var workflow = new AiMetadataWorkflow(services.GetRequiredService<AppSettings>(), services.GetRequiredService<AppPaths>(),
            new NoCredentialStore(), services.GetRequiredService<ResourceLocator>().TemplatesFolder);
        workflow.AddImages([Jpeg("ai.jpg")]);
        var vm = new AiMetadataViewModel(workflow, services.GetRequiredService<IDialogService>(), services.GetRequiredService<IStorageService>(),
            services.GetRequiredService<AppSettings>(), services.GetRequiredService<AppPaths>());
        var window = new AiMetadataWindow { DataContext = vm };
        window.Show();
        Assert.Contains("openai-economy", vm.TemplateText, StringComparison.Ordinal);
        Assert.Single(vm.VisibleRows);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ImageEditor_OpensImage_AndExports()
    {
        ShowMain(out _);
        var vm = new ImageEditorViewModel(Jpeg("edit.jpg"), new GeoCoordinate(1, 2), services.GetRequiredService<IDialogService>());
        var window = new ImageEditorWindow { DataContext = vm };
        window.Show(); // Opened loads the image
        for (int i = 0; i < 200 && vm.DisplayWidth == 0; i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
        Assert.Equal(64, vm.DisplayWidth);
        vm.PointerDown(5, 5);
        vm.PointerMove(20, 30);
        Assert.True(vm.HasSelection);
        await vm.ApplyCommand.ExecuteAsync(null); // crop
        Assert.Equal(15, vm.DisplayWidth);
        Assert.True(await vm.ExportAsync());
        Assert.Equal(".jpg", vm.Result!.Value.Extension);
        window.Close();
    }

    [AvaloniaFact]
    public void RoadSettingsAndLicenseWindows_Load()
    {
        ShowMain(out _);
        var road = new RoadMatchSettingsWindow { DataContext = new RoadMatchSettingsViewModel(services.GetRequiredService<AppSettings>()) };
        road.Show();
        road.Close();
        var license = new LicenseWindow { DataContext = new LicenseViewModel(services.GetRequiredService<ResourceLocator>()) };
        license.Show();
        Assert.Contains("MIT", ((LicenseViewModel)license.DataContext!).License, StringComparison.Ordinal);
        license.Close();
    }
}
