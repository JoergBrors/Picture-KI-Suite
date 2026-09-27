using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PictureGeoExif.Application;
using PictureGeoExif.Application.Diagnostics;
using PictureGeoExif.Application.Logging;
using PictureGeoExif.Application.Photos;
using PictureGeoExif.Application.Platform;
using PictureGeoExif.Application.Sources;
using PictureGeoExif.Core;
using PictureGeoExif.Core.Imaging;
using PictureGeoExif.Core.Photos;
using PictureGeoExif.Core.Platform;
using PictureGeoExif.Core.Security;
using PictureGeoExif.Desktop.Services;
using PictureGeoExif.Desktop.ViewModels;
using PictureGeoExif.Metadata;
using PictureGeoExif.Metadata.Ai;
using PictureGeoExif.Metadata.Decoding;
using PictureGeoExif.Platform.Mac;
using PictureGeoExif.Platform.Mac.PhotoKit;
using PictureGeoExif.Platform.Windows;

namespace PictureGeoExif.Desktop;

/// <summary>Dependency injection setup. Platform adapters are chosen here and nowhere else.</summary>
public static class Bootstrap
{
    public static string UiFramework => "Avalonia " + AppInfo.LibraryVersion(typeof(global::Avalonia.Application));

    public static ServiceProvider BuildServices(AppPaths? paths = null, bool fileLogging = true)
    {
        paths ??= AppPaths.Current;
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
            if (fileLogging)
            {
                try { builder.AddProvider(new FileLoggerProvider(paths.LogFolder)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* log folder not writable: console only */ }
            }
        });
        services.AddSingleton(paths);
        services.AddSingleton(_ =>
        {
            var settings = AppSettings.Load();
            if (string.IsNullOrEmpty(settings.OutputFolder)) settings.OutputFolder = AppPaths.DefaultOutputFolder();
            return settings;
        });
        AiMetadataService.CacheFolder = paths.AiCacheFolder;

        var decoders = new List<IImageDecoder> { new ImageSharpDecoder() };
        if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<IPlatformInfo, MacPlatformInfo>();
            services.AddSingleton<IPhotoKitFacade, ObjCPhotoKitFacade>();
            services.AddSingleton<IPhotoLibraryService, MacPhotoLibraryService>();
            services.AddSingleton<ICredentialStore, MacKeychainCredentialStore>();
            decoders.Add(new MacImageIODecoder());
        }
        else if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IPlatformInfo, DefaultPlatformInfo>();
            services.AddSingleton<IPhotoLibraryService, UnavailablePhotoLibraryService>();
            services.AddSingleton<ICredentialStore, WindowsCredentialStore>();
        }
        else
        {
            services.AddSingleton<IPlatformInfo, DefaultPlatformInfo>();
            services.AddSingleton<IPhotoLibraryService, UnavailablePhotoLibraryService>();
            services.AddSingleton<ICredentialStore, NoCredentialStore>();
        }
        services.AddSingleton<IImageDecoder>(new CompositeImageDecoder(decoders));
        services.AddSingleton(sp => new ImageService(sp.GetRequiredService<IImageDecoder>()));
        services.AddSingleton(sp => new PhotoOriginalCache(sp.GetRequiredService<IPhotoLibraryService>(), paths.PhotosOriginalCacheFolder,
            sp.GetRequiredService<ILogger<PhotoOriginalCache>>()));
        services.AddSingleton<FileSystemImageSourceProvider>();
        services.AddSingleton(sp => new DiagnosticsService(sp.GetRequiredService<IPlatformInfo>(), sp.GetRequiredService<IPhotoLibraryService>(),
            sp.GetRequiredService<IImageDecoder>(), UiFramework, paths));
        services.AddSingleton<ResourceLocator>();
        services.AddSingleton<UiContext>();
        services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<UiContext>());
        services.AddSingleton<IStorageService>(sp => sp.GetRequiredService<UiContext>());
        services.AddSingleton<IWindowService, WindowService>();
        services.AddSingleton<MapViewModel>();
        services.AddSingleton<PhotosBrowserViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        return services.BuildServiceProvider();
    }

    /// <summary>Startup facts for diagnosing macOS issues. No user data (no paths of pictures, no GPS, no asset ids).</summary>
    public static async Task LogStartup(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        var platform = services.GetRequiredService<IPlatformInfo>();
        var photos = services.GetRequiredService<IPhotoLibraryService>();
        var status = await photos.GetAuthorizationStatusAsync();
        logger.LogInformation(
            "{Product} {Version} | .NET {Runtime} | OS {Os} {OsVersion} | Process Architecture {Arch} | ARM64 {Arm} | {Ui} | " +
            "PhotoKit verfügbar {PhotoKit} | Photo Library Authorization Status {Status} | App Bundle {Bundle} | Resources {Resources}",
            AppInfo.ProductName, AppInfo.Version, RuntimeInformation.FrameworkDescription, platform.OperatingSystem, platform.OperatingSystemVersion,
            platform.ProcessArchitecture, platform.IsArm64 ? "ja" : "nein", UiFramework, photos.IsAvailable ? "ja" : "nein", status,
            platform.AppBundlePath ?? "(kein Bundle)", platform.ResourcesPath);
    }
}
