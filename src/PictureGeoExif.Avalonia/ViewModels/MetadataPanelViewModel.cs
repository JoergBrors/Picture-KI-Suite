using CommunityToolkit.Mvvm.ComponentModel;
using PictureGeoExif.Metadata;

namespace PictureGeoExif.Desktop.ViewModels;

/// <summary>Metadata tabs: General, EXIF, GPS, XMP, IPTC, Raw.</summary>
public sealed partial class MetadataPanelViewModel : ObservableObject
{
    [ObservableProperty] private string title = "Kein Bild ausgewählt";
    [ObservableProperty] private string? error;
    [ObservableProperty] private IReadOnlyList<MetadataEntry> general = [];
    [ObservableProperty] private IReadOnlyList<MetadataEntry> exif = [];
    [ObservableProperty] private IReadOnlyList<MetadataEntry> gps = [];
    [ObservableProperty] private IReadOnlyList<MetadataEntry> xmp = [];
    [ObservableProperty] private IReadOnlyList<MetadataEntry> iptc = [];
    [ObservableProperty] private IReadOnlyList<MetadataEntry> raw = [];
    [ObservableProperty] private MetadataReport? report;

    public void Show(MetadataReport value, IEnumerable<MetadataEntry>? extraGeneral = null)
    {
        Report = value;
        Title = value.FileName;
        Error = value.Error;
        General = [.. value.Group(MetadataGroup.General), .. extraGeneral ?? []];
        Exif = value.Group(MetadataGroup.Exif).ToList();
        Gps = value.Group(MetadataGroup.Gps).ToList();
        Xmp = value.Group(MetadataGroup.Xmp).ToList();
        Iptc = value.Group(MetadataGroup.Iptc).ToList();
        Raw = value.Group(MetadataGroup.Raw).ToList();
    }

    public void Clear(string title, string? error = null, IEnumerable<MetadataEntry>? general = null)
    {
        Report = null;
        Title = title;
        Error = error;
        General = general?.ToList() ?? [];
        Exif = Gps = Xmp = Iptc = Raw = [];
    }
}
