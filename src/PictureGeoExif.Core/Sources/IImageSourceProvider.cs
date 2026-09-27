namespace PictureGeoExif.Core.Sources;

/// <summary>Common access to images from files and from Apple Photos, so the rest of the app does not care about the origin.</summary>
public interface IImageSourceProvider
{
    string Name { get; }

    Task<IReadOnlyList<ImageSourceItem>> GetImagesAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the original bytes read-only.</summary>
    Task<Stream> OpenImageAsync(ImageSourceItem image, CancellationToken cancellationToken = default);
}

/// <summary>Sources with potentially huge listings (photo libraries) are paged.</summary>
public interface IPagedImageSourceProvider : IImageSourceProvider
{
    Task<IReadOnlyList<ImageSourceItem>> GetPageAsync(int offset, int count, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
