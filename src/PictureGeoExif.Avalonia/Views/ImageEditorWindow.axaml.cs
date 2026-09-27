using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PictureGeoExif.Desktop.ViewModels;
using PictureGeoExif.Metadata.Editing;

namespace PictureGeoExif.Desktop.Views;

/// <summary>Rendering and pointer input only; all pixel work happens in <see cref="ImageEditorViewModel"/> and the editor engine.</summary>
public partial class ImageEditorWindow : Window
{
    private bool selecting, accepting, fit = true;

    public ImageEditorWindow()
    {
        InitializeComponent();
        Surface.PointerPressed += Surface_PointerPressed;
        Surface.PointerMoved += Surface_PointerMoved;
        Surface.PointerReleased += (_, e) => { selecting = false; e.Pointer.Capture(null); };
        Viewport.SizeChanged += (_, _) => { if (fit) Fit(); };
        Opened += async (_, _) =>
        {
            if (ViewModel is not { } vm) return;
            vm.PropertyChanged += OnViewModelChanged;
            await vm.LoadAsync();
            Fit();
        };
        Closed += (_, _) => ViewModel?.Dispose();
        KeyDown += OnKeyDown;
    }

    private ImageEditorViewModel? ViewModel => DataContext as ImageEditorViewModel;

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        switch (e.PropertyName)
        {
            case nameof(ImageEditorViewModel.Selection):
                Canvas.SetLeft(SelectionBox, vm.Selection.X); Canvas.SetTop(SelectionBox, vm.Selection.Y);
                SelectionBox.Width = vm.Selection.Width; SelectionBox.Height = vm.Selection.Height;
                SelectionBox.StrokeThickness = 1.5 / vm.Zoom;
                break;
            case nameof(ImageEditorViewModel.ClickX) or nameof(ImageEditorViewModel.ClickY):
                Canvas.SetLeft(ClickMarker, vm.ClickX - 4); Canvas.SetTop(ClickMarker, vm.ClickY - 4);
                break;
            case nameof(ImageEditorViewModel.DisplayWidth) or nameof(ImageEditorViewModel.DisplayHeight):
                if (fit) Fit();
                break;
        }
    }

    private void Surface_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } vm || !e.GetCurrentPoint(Surface).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(Surface);
        vm.PointerDown(p.X, p.Y);
        selecting = vm.NeedsSelection;
        if (selecting) e.Pointer.Capture(Surface);
        e.Handled = true;
    }

    private void Surface_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!selecting || ViewModel is not { } vm) return;
        var p = e.GetPosition(Surface);
        vm.PointerMove(p.X, p.Y);
    }

    private void Fit()
    {
        if (ViewModel is not { DisplayWidth: > 0 } vm) return;
        fit = true;
        vm.Zoom = PixelGeometry.Fit(vm.DisplayWidth, vm.DisplayHeight, Viewport.Bounds.Width, Viewport.Bounds.Height);
    }

    private void Fit_Click(object? sender, RoutedEventArgs e) => Fit();
    private void Actual_Click(object? sender, RoutedEventArgs e) { fit = false; if (ViewModel is { } vm) vm.Zoom = 1; }
    private void ZoomIn_Click(object? sender, RoutedEventArgs e) { fit = false; if (ViewModel is { } vm) vm.Zoom = Math.Min(16, vm.Zoom * 1.2); }
    private void ZoomOut_Click(object? sender, RoutedEventArgs e) { fit = false; if (ViewModel is { } vm) vm.Zoom = Math.Max(0.00001, vm.Zoom / 1.2); }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && await vm.ExportAsync()) { accepting = true; Close(true); }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (accepting || ViewModel is not { } vm) return;
        if (vm.IsBusy) { vm.CancelOperationCommand.Execute(null); e.Cancel = true; return; }
        if (vm.HasChanges && !e.IsProgrammatic)
        {
            e.Cancel = true;
            var dialog = new Services.MessageDialog("Bildeditor", "Ungespeicherte Änderungen verwerfen?", "Verwerfen", "Abbrechen");
            if (await dialog.ShowDialog<bool>(this)) { accepting = true; Close(false); }
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm || e.Source is TextBox) return;
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.Key == Key.Escape) { vm.ClearCommand.Execute(null); e.Handled = true; }
        else if (e.KeyModifiers == command && e.Key == Key.Z) { vm.UndoCommand.Execute(null); e.Handled = true; }
        else if (e.KeyModifiers == command && e.Key == Key.Y) { vm.RedoCommand.Execute(null); e.Handled = true; }
    }
}
