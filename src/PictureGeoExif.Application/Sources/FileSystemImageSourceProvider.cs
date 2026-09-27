using PictureGeoExif.Core.Imaging;
using PictureGeoExif.Core.Sources;

namespace PictureGeoExif.Application.Sources;

/// <summary>Files added via dialog, folder selection or drag &amp; drop from Finder/Explorer.</summary>
public sealed class FileSystemImageSourceProvider : IImageSourceProvider
{
    private readonly List<ImageSourceItem> items = [];
    private readonly Lock gate = new();

    public string Name => "Dateien";

    /// <summary>Adds supported, not yet listed files. Returns the new items in input order.</summary>
    public IReadOnlyList<ImageSourceItem> Add(IEnumerable<string> paths)
    {
        var added = new List<ImageSourceItem>();
        lock (gate)
        {
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !ImageFormatDetector.IsSupportedExtension(path)) continue;
                string full = Path.GetFullPath(path);
                if (items.Any(i => string.Equals(i.SourceId, full, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))) continue;
                var item = ImageSourceItem.FromFile(full);
                items.Add(item);
                added.Add(item);
            }
        }
        return added;
    }

    /// <summary>Supported images directly inside <paramref name="folder"/> (optionally recursive), sorted by name.</summary>
    public static IReadOnlyList<string> EnumerateFolder(string folder, bool recursive = false)
    {
        if (!Directory.Exists(folder)) return [];
        var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        return Directory.EnumerateFiles(folder, "*", options).Where(ImageFormatDetector.IsSupportedExtension)
            .Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void Remove(ImageSourceItem item) { lock (gate) items.Remove(item); }
    public void Clear() { lock (gate) items.Clear(); }

    /// <summary>Replaces an entry after a save created a new copy (list shows the newest version).</summary>
    public ImageSourceItem Replace(ImageSourceItem old, string newPath)
    {
        var updated = ImageSourceItem.FromFile(newPath);
        lock (gate)
        {
            int index = items.IndexOf(old);
            if (index >= 0) items[index] = updated; else items.Add(updated);
        }
        return updated;
    }

    public Task<IReadOnlyList<ImageSourceItem>> GetImagesAsync(CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<ImageSourceItem>>(items.ToList());
    }

    public Task<Stream> OpenImageAsync(ImageSourceItem image, CancellationToken cancellationToken = default)
    {
        if (image.LocalPath == null) throw new InvalidOperationException("Datei ohne lokalen Pfad.");
        return Task.FromResult<Stream>(new FileStream(image.LocalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true));
    }
}
