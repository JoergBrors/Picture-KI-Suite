namespace PictureGeoExif.Core;

/// <summary>Version and identity derived from the shared &lt;Version&gt; in Directory.Build.props, so tags, UI, Info.plist and HTTP identification never drift apart.</summary>
public static class AppInfo
{
    public const string ProductName = "PictureGeoExif";
    public const string BundleIdentifier = "net.brors.picturegeoexif";
    public const string RepositoryUrl = "https://github.com/JoergBrors/PictureGeoExif";
    public static string Version => typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    /// <summary>Honest app identification appended to HTTP user agents (OSM tile policy, routing server, AI providers).</summary>
    public static string UserAgent => $"{ProductName}/{Version} (+{RepositoryUrl})";
}
