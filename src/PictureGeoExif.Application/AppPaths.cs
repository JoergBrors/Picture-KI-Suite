namespace PictureGeoExif.Application;

/// <summary>
/// Where the app keeps settings, caches and logs. Windows keeps the historical PictureGeoExif locations so existing
/// settings continue to work; macOS follows the Apple conventions (Application Support, Caches, Logs).
/// </summary>
public sealed class AppPaths
{
    public required string SettingsFolder { get; init; }
    public required string DataFolder { get; init; }
    public required string CacheFolder { get; init; }
    public required string LogFolder { get; init; }

    public string SettingsFile => Path.Combine(SettingsFolder, "settings.json");
    public string UserTemplatesFolder => Path.Combine(SettingsFolder, "templates");
    public string AiCacheFolder => Path.Combine(DataFolder, "ai-cache");
    public string AuditFolder => Path.Combine(DataFolder, "ai-audit");
    /// <summary>Local copies of Photos originals used for metadata analysis; can be deleted at any time.</summary>
    public string PhotosOriginalCacheFolder => Path.Combine(CacheFolder, "photos-originals");
    public string MapTileCacheFolder => Path.Combine(CacheFolder, "map-tiles");

    private static AppPaths? current;
    public static AppPaths Current { get => current ??= ForCurrentPlatform(); set => current = value; }

    public static AppPaths ForCurrentPlatform()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            string library = Path.Combine(home, "Library");
            string support = Path.Combine(library, "Application Support", "PictureGeoExif");
            return new AppPaths
            {
                SettingsFolder = support,
                DataFolder = support,
                CacheFolder = Path.Combine(library, "Caches", "PictureGeoExif"),
                LogFolder = Path.Combine(library, "Logs", "PictureGeoExif")
            };
        }
        if (OperatingSystem.IsWindows())
        {
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PictureGeoExif");
            return new AppPaths
            {
                SettingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PictureExifclone"),
                DataFolder = local,
                CacheFolder = Path.Combine(local, "cache"),
                LogFolder = Path.Combine(local, "logs")
            };
        }
        string Xdg(string variable, string fallback) => Environment.GetEnvironmentVariable(variable) is { Length: > 0 } v ? v : Path.Combine(home, fallback);
        return new AppPaths
        {
            SettingsFolder = Path.Combine(Xdg("XDG_CONFIG_HOME", ".config"), "PictureGeoExif"),
            DataFolder = Path.Combine(Xdg("XDG_DATA_HOME", Path.Combine(".local", "share")), "PictureGeoExif"),
            CacheFolder = Path.Combine(Xdg("XDG_CACHE_HOME", ".cache"), "PictureGeoExif"),
            LogFolder = Path.Combine(Xdg("XDG_STATE_HOME", Path.Combine(".local", "state")), "PictureGeoExif", "logs")
        };
    }

    /// <summary>Default output folder for copies with GPS and editor exports.</summary>
    public static string DefaultOutputFolder() => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PictureGeoExif_Output")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PictureExifclone_Output");
}
