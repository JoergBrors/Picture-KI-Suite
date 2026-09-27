using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PictureGeoExif.Application;
using PictureGeoExif.Application.Ai;
using PictureGeoExif.Application.Diagnostics;
using PictureGeoExif.Core.Geo;
using PictureGeoExif.Core.Security;
using PictureGeoExif.Desktop.ViewModels;
using PictureGeoExif.Desktop.Views;

namespace PictureGeoExif.Desktop.Services;

public interface IWindowService
{
    void ShowDiagnostics();
    void ShowLicenses();
    void ShowAiMetadata(IReadOnlyList<string> localPaths);
    /// <summary>Opens the editor; returns the exported bytes and extension, or null if cancelled.</summary>
    Task<(byte[] Bytes, string Extension)?> ShowEditorAsync(string path, GeoCoordinate? gps);
    Task<bool> ShowRoadSettingsAsync();
}

public sealed class WindowService(IServiceProvider services, UiContext ui) : IWindowService
{
    public void ShowDiagnostics()
    {
        var vm = new DiagnosticsViewModel(services.GetRequiredService<DiagnosticsService>(), services.GetRequiredService<IStorageService>(),
            services.GetRequiredService<Application.Photos.PhotoOriginalCache>(), services.GetRequiredService<AppPaths>());
        var window = new DiagnosticsWindow { DataContext = vm };
        _ = vm.LoadAsync();
        Show(window);
    }

    public void ShowLicenses() => Show(new LicenseWindow { DataContext = new LicenseViewModel(services.GetRequiredService<ResourceLocator>()) });

    public void ShowAiMetadata(IReadOnlyList<string> localPaths)
    {
        var workflow = new AiMetadataWorkflow(services.GetRequiredService<AppSettings>(), services.GetRequiredService<AppPaths>(),
            services.GetRequiredService<ICredentialStore>(), services.GetRequiredService<ResourceLocator>().TemplatesFolder,
            services.GetRequiredService<ILogger<AiMetadataWorkflow>>());
        workflow.AddImages(localPaths);
        var vm = new AiMetadataViewModel(workflow, services.GetRequiredService<IDialogService>(), services.GetRequiredService<IStorageService>(),
            services.GetRequiredService<AppSettings>(), services.GetRequiredService<AppPaths>());
        Show(new AiMetadataWindow { DataContext = vm });
    }

    public async Task<(byte[] Bytes, string Extension)?> ShowEditorAsync(string path, GeoCoordinate? gps)
    {
        if (ui.Owner == null) return null;
        var vm = new ImageEditorViewModel(path, gps, services.GetRequiredService<IDialogService>());
        var window = new ImageEditorWindow { DataContext = vm };
        bool accepted = await window.ShowDialog<bool>(ui.Owner);
        return accepted && vm.Result is { } result ? result : null;
    }

    public async Task<bool> ShowRoadSettingsAsync()
    {
        if (ui.Owner == null) return false;
        var window = new RoadMatchSettingsWindow { DataContext = new RoadMatchSettingsViewModel(services.GetRequiredService<AppSettings>()) };
        return await window.ShowDialog<bool>(ui.Owner);
    }

    private void Show(global::Avalonia.Controls.Window window)
    {
        if (ui.Owner != null) window.Show(ui.Owner); else window.Show();
    }
}
