using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PictureGeoExif.Core.Geo;
using PictureGeoExif.Core.Imaging;
using PictureGeoExif.Core.Sources;

namespace PictureGeoExif.Desktop.ViewModels;

/// <summary>One image in a list, independent of its origin (file or Apple Photos).</summary>
public sealed partial class ImageEntryViewModel(ImageSourceItem source) : ObservableObject
{
    private readonly Stack<(ImageSourceItem Source, GeoCoordinate? Location)> history = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName), nameof(Details), nameof(IsPhotosAsset), nameof(LocalPath), nameof(CanEdit), nameof(SourceLabel))]
    private ImageSourceItem source = source;

    [ObservableProperty] private Bitmap? thumbnail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpsText), nameof(HasGps))]
    private GeoCoordinate? location = source.Location;

    [ObservableProperty] private string routeInfo = "";
    [ObservableProperty] private bool isSelectedOnMap;

    /// <summary>Local copy of a Photos original after it was loaded for analysis (cache, read-only).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocalPath), nameof(CanEdit))]
    private string? materializedPath;

    [ObservableProperty] private string? loadError;
    [ObservableProperty] private double? roadDistance;

    public string FileName => Source.OriginalFileName;
    public bool IsPhotosAsset => Source.SourceType == ImageSourceType.ApplePhotos;
    public string SourceLabel => IsPhotosAsset ? "Apple Fotos (schreibgeschützt)" : "Datei";
    /// <summary>Local file for metadata analysis: the file itself, or the cached Photos original.</summary>
    public string? LocalPath => Source.LocalPath ?? MaterializedPath;
    /// <summary>Editing always works on files; Photos originals must be exported first (never modified in place).</summary>
    public bool CanEdit => !IsPhotosAsset && Source.CanEdit;
    public bool HasGps => Location != null;
    public string GpsText => Location is { } gps ? "📍 " + gps : "Keine GPS-Daten";

    public string Details
    {
        get
        {
            var parts = new List<string> { ImageFormatCapabilities.DisplayName(Source.OriginalFormat) };
            if (Source.PixelWidth is > 0 && Source.PixelHeight is > 0) parts.Add($"{Source.PixelWidth} × {Source.PixelHeight}");
            if (Source.CreationDate is { } date) parts.Add(date.LocalDateTime.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("de-DE")));
            if (Source.IsFavorite) parts.Add("♥ Favorit");
            return string.Join(" · ", parts);
        }
    }

    // Virtual route position (files with GPS only).
    public int RouteNumber { get; private set; }
    public int RouteIndex { get; private set; }
    public bool IsBranch { get; private set; }

    public void SetRoute(int number, int index, bool branch)
    {
        RouteNumber = number; RouteIndex = index; IsBranch = branch;
        RouteInfo = number > 0 ? $"Trasse {number} · Nr. {index}{(branch ? " · Abzweig" : "")}" : "";
    }

    public bool CanUndo => history.Count > 0;

    /// <summary>Remember the current state before a save/GPS change so it can be undone.</summary>
    public void PushHistory()
    {
        history.Push((Source, Location));
        OnPropertyChanged(nameof(CanUndo));
    }

    /// <summary>Restores the previous file and coordinates. Returns the path that was active before undo (the saved copy stays on disk).</summary>
    public string? Undo()
    {
        if (history.Count == 0) return null;
        string? current = Source.LocalPath;
        var previous = history.Pop();
        Source = previous.Source; Location = previous.Location;
        OnPropertyChanged(nameof(CanUndo));
        return current;
    }
}
