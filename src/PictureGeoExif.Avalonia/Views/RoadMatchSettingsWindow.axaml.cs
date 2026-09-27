using Avalonia.Controls;
using Avalonia.Interactivity;
using PictureGeoExif.Desktop.ViewModels;

namespace PictureGeoExif.Desktop.Views;

public partial class RoadMatchSettingsWindow : Window
{
    public RoadMatchSettingsWindow() => InitializeComponent();

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RoadMatchSettingsViewModel vm && vm.TrySave()) Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
