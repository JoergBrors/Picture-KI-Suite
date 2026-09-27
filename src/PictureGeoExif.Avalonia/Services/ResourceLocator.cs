using PictureGeoExif.Core.Platform;

namespace PictureGeoExif.Desktop.Services;

/// <summary>Finds shipped files (Templates, licenses) in Contents/Resources of a macOS bundle or next to the executable.</summary>
public sealed class ResourceLocator(IPlatformInfo platform)
{
    public string Find(string relative)
    {
        foreach (var root in new[] { platform.ResourcesPath, AppContext.BaseDirectory }.Distinct())
        {
            string candidate = Path.Combine(root, relative);
            if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
        }
        return Path.Combine(AppContext.BaseDirectory, relative);
    }

    public string TemplatesFolder => Find("Templates");
}
