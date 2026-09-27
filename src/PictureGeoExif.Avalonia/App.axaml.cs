using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PictureGeoExif.Desktop.ViewModels;
using PictureGeoExif.Desktop.Views;

namespace PictureGeoExif.Desktop;

// Fully qualified base type: inside PictureGeoExif.* the simple name "Application" would resolve to the PictureGeoExif.Application namespace.
public partial class App : global::Avalonia.Application
{
    /// <summary>Set by <see cref="Program"/> before Avalonia starts; tests may assign their own container.</summary>
    public static IServiceProvider? Services { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Services != null)
        {
            var viewModel = Services.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow { DataContext = viewModel };
            Services.GetRequiredService<Services.UiContext>().Attach(window);
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => viewModel.OnShutdown();
            _ = viewModel.InitializeAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
