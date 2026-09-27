using Avalonia;
using Avalonia.Controls;
using PictureGeoExif.Metadata;

namespace PictureGeoExif.Desktop.Views;

/// <summary>Read-only table of metadata entries (one tab of the metadata panel).</summary>
public partial class MetadataTable : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<MetadataEntry>?> EntriesProperty =
        AvaloniaProperty.Register<MetadataTable, IReadOnlyList<MetadataEntry>?>(nameof(Entries));

    public MetadataTable() => InitializeComponent();

    public IReadOnlyList<MetadataEntry>? Entries
    {
        get => GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }
}
