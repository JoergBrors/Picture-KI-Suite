using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureGeoExif.Application;
using PictureGeoExif.Application.Diagnostics;
using PictureGeoExif.Application.Photos;
using PictureGeoExif.Core.Geo;
using PictureGeoExif.Desktop.Services;
using PictureGeoExif.Metadata;

namespace PictureGeoExif.Desktop.ViewModels;

/// <summary>Help → Diagnostics.</summary>
public sealed partial class DiagnosticsViewModel(DiagnosticsService diagnostics, IStorageService storage, PhotoOriginalCache originals, AppPaths paths) : ObservableObject
{
    public ObservableCollection<DiagnosticsItem> Items { get; } = [];
    [ObservableProperty] private string status = "";

    public async Task LoadAsync()
    {
        Items.Clear();
        foreach (var item in await diagnostics.CollectAsync()) Items.Add(item);
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        await storage.SetClipboardTextAsync(DiagnosticsService.Format(Items));
        Status = "Diagnoseinformationen in die Zwischenablage kopiert.";
    }

    [RelayCommand]
    private void ClearPhotosCache()
    {
        long freed = originals.Clear();
        Status = $"Cache der Apple-Fotos-Originale geleert ({MetadataInspector.FormatSize(freed)}).";
    }

    [RelayCommand]
    private async Task OpenLogFolderAsync()
    {
        Directory.CreateDirectory(paths.LogFolder);
        await storage.OpenUrlAsync(new Uri(paths.LogFolder + Path.DirectorySeparatorChar));
    }
}

/// <summary>License texts shipped with the app.</summary>
public sealed class LicenseViewModel
{
    public LicenseViewModel(ResourceLocator resources)
    {
        License = Read(resources.Find("LICENSE"));
        ThirdParty = Read(resources.Find("THIRD-PARTY-LICENSES.md"));
        string folder = resources.Find("licenses");
        LicensesInfo = Directory.Exists(folder)
            ? $"licenses-Ordner gefunden ({Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories).Length} Dateien): {folder}"
            : "licenses-Ordner nicht gefunden";
    }

    public string License { get; }
    public string ThirdParty { get; }
    public string LicensesInfo { get; }

    private static string Read(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : "Datei nicht gefunden: " + path; }
        catch (IOException ex) { return ex.Message; }
    }
}

/// <summary>Valhalla map-matching server settings.</summary>
public sealed partial class RoadMatchSettingsViewModel(AppSettings settings) : ObservableObject
{
    public static IReadOnlyList<string> Profiles { get; } = ["pedestrian", "bicycle", "auto"];

    [ObservableProperty] private string url = settings.RoadMatchUrl;
    [ObservableProperty] private string profile = Profiles.Contains(settings.RoadMatchProfile) ? settings.RoadMatchProfile : "pedestrian";
    [ObservableProperty] private double deviation = Math.Clamp(settings.RoadMatchMaxDeviationMeters, 5, 200);
    [ObservableProperty] private string? error;

    [RelayCommand]
    private void Defaults()
    {
        Url = RoadMatcher.DefaultServer; Profile = "pedestrian"; Deviation = 25;
    }

    /// <summary>Validates and stores; returns false with <see cref="Error"/> set if the URL is not allowed.</summary>
    public bool TrySave()
    {
        if (!RoadMatcher.IsAllowedServer(Url, out var uri))
        {
            Error = "Bitte eine HTTPS-Adresse eintragen (HTTP nur für einen Server auf diesem Rechner, z. B. http://localhost:8002).";
            return false;
        }
        settings.RoadMatchUrl = uri.AbsoluteUri.TrimEnd('/');
        settings.RoadMatchProfile = Profile;
        settings.RoadMatchMaxDeviationMeters = Deviation;
        settings.Save();
        return true;
    }
}
