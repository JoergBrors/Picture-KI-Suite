using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PictureGeoExif.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var services = Bootstrap.BuildServices();
        App.Services = services;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => logger.LogCritical(e.ExceptionObject as Exception, "Unbehandelte Ausnahme.");
        TaskScheduler.UnobservedTaskException += (_, e) => { logger.LogError(e.Exception, "Unbeobachtete Task-Ausnahme."); e.SetObserved(); };
        try
        {
            Bootstrap.LogStartup(services).GetAwaiter().GetResult();
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Start fehlgeschlagen.");
            throw;
        }
    }

    /// <summary>Also used by the Avalonia designer/previewer.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
