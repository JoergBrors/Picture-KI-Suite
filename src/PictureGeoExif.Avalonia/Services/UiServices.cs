using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PictureGeoExif.Core.Imaging;

namespace PictureGeoExif.Desktop.Services;

public interface IDialogService
{
    Task ShowMessageAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message, string yes = "Ja", string no = "Nein");
}

/// <summary>File and folder pickers through Avalonia's StorageProvider (native NSOpenPanel/NSSavePanel on macOS).</summary>
public interface IStorageService
{
    Task<IReadOnlyList<string>> PickImagesAsync(bool multiple = true, string title = "Bilder auswählen");
    Task<string?> PickFolderAsync(string title, string? start = null);
    Task<string?> PickSaveFileAsync(string title, string suggestedName, string? start = null);
    Task<string?> PickJsonAsync(string title, string? start = null);
    Task SetClipboardTextAsync(string text);
    Task OpenUrlAsync(Uri uri);
}

/// <summary>Connects view models with the active window (dialogs, pickers, clipboard). The owner is attached at startup.</summary>
public sealed class UiContext : IDialogService, IStorageService
{
    private Window? owner;

    public Window? Owner => owner;
    public void Attach(Window window) => owner = window;

    private TopLevel TopLevel => owner ?? throw new InvalidOperationException("Kein Hauptfenster.");

    public async Task ShowMessageAsync(string title, string message)
    {
        if (owner == null) return;
        await new MessageDialog(title, message, "OK", null).ShowDialog<bool>(owner);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string yes = "Ja", string no = "Nein")
    {
        if (owner == null) return false;
        return await new MessageDialog(title, message, yes, no).ShowDialog<bool>(owner);
    }

    private static readonly FilePickerFileType ImageFiles = new("Bilddateien")
    {
        Patterns = ImageFormatDetector.SupportedExtensions.Select(e => "*" + e).ToList(),
        AppleUniformTypeIdentifiers = ["public.image"],
        MimeTypes = ["image/*"]
    };

    public async Task<IReadOnlyList<string>> PickImagesAsync(bool multiple = true, string title = "Bilder auswählen")
    {
        var files = await TopLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = multiple, FileTypeFilter = [ImageFiles, FilePickerFileTypes.All]
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    private async Task<IStorageFolder?> Folder(string? path) =>
        path != null && Directory.Exists(path) ? await TopLevel.StorageProvider.TryGetFolderFromPathAsync(path) : null;

    public async Task<string?> PickFolderAsync(string title, string? start = null)
    {
        var folders = await TopLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title, AllowMultiple = false, SuggestedStartLocation = await Folder(start)
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, string? start = null)
    {
        var file = await TopLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title, SuggestedFileName = suggestedName, SuggestedStartLocation = await Folder(start),
            ShowOverwritePrompt = true, DefaultExtension = Path.GetExtension(suggestedName).TrimStart('.')
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickJsonAsync(string title, string? start = null)
    {
        var files = await TopLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = false, SuggestedStartLocation = await Folder(start),
            FileTypeFilter = [new FilePickerFileType("JSON-Vorlage") { Patterns = ["*.json"], AppleUniformTypeIdentifiers = ["public.json"] }]
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task SetClipboardTextAsync(string text)
    {
        if (TopLevel.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    public async Task OpenUrlAsync(Uri uri) => await TopLevel.Launcher.LaunchUriAsync(uri);
}

/// <summary>Small modal message/confirmation dialog (Avalonia has no built-in MessageBox).</summary>
public sealed class MessageDialog : Window
{
    public MessageDialog(string title, string message, string ok, string? cancel)
    {
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 16, 0, 0) };
        if (cancel != null)
        {
            var no = new Button { Content = cancel, IsCancel = true };
            no.Click += (_, _) => Close(false);
            buttons.Children.Add(no);
        }
        var yes = new Button { Content = ok, IsDefault = true, Classes = { "accent" } };
        yes.Click += (_, _) => Close(true);
        buttons.Children.Add(yes);
        Content = new StackPanel
        {
            Margin = new(20),
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 16, Margin = new(0, 0, 0, 10) },
                new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                buttons
            }
        };
    }
}
