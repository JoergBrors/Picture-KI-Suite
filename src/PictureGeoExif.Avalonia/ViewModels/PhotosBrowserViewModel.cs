using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PictureGeoExif.Application.Sources;
using PictureGeoExif.Core.Photos;
using PictureGeoExif.Core.Sources;

namespace PictureGeoExif.Desktop.ViewModels;

/// <summary>
/// "Apple Fotos" area: permission status, albums and paged asset listing with lazily loaded thumbnails.
/// Talks only to <see cref="IPhotoLibraryService"/>; never to PhotoKit directly. Access is only requested on a user action.
/// </summary>
public sealed partial class PhotosBrowserViewModel(IPhotoLibraryService photos, ILogger<PhotosBrowserViewModel> logger) : ObservableObject
{
    public const int PageSize = ApplePhotosImageSourceProvider.DefaultPageSize;
    private const int ThumbnailSize = 256;
    private CancellationTokenSource? listing;
    private ApplePhotosImageSourceProvider? provider;

    public bool IsAvailable => photos.IsAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(CanRequestAccess), nameof(CanOpenLibrary), nameof(IsDenied))]
    [NotifyCanExecuteChangedFor(nameof(RequestAccessCommand), nameof(OpenLibraryCommand))]
    private PhotoLibraryAccessStatus status = PhotoLibraryAccessStatus.Unavailable;

    public string StatusText => "Status: " + Status.DisplayText();
    public bool CanRequestAccess => Status == PhotoLibraryAccessStatus.NotDetermined;
    public bool CanOpenLibrary => Status.CanRead();
    public bool IsDenied => Status is PhotoLibraryAccessStatus.Denied or PhotoLibraryAccessStatus.Restricted;

    public ObservableCollection<PhotoAlbum> Albums { get; } = [];
    public ObservableCollection<ImageEntryViewModel> Assets { get; } = [];

    [ObservableProperty] private PhotoAlbum? selectedAlbum;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMore), nameof(CountText))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private int totalCount;

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string? message;

    public bool HasMore => Assets.Count < TotalCount;
    public string CountText => TotalCount == 0 ? "" : $"{Assets.Count} von {TotalCount} Fotos";

    /// <summary>Reads the current status without prompting (safe at startup).</summary>
    public async Task RefreshStatusAsync()
    {
        Status = await photos.GetAuthorizationStatusAsync();
        Message = !photos.IsAvailable ? "Apple Fotos ist nur unter macOS verfügbar. Dateien können weiterhin normal geöffnet werden." : null;
    }

    [RelayCommand(CanExecute = nameof(CanRequestAccess))]
    private async Task RequestAccessAsync()
    {
        if (!photos.CanRequestAuthorization)
        {
            Message = "Der Zugriff kann nur angefordert werden, wenn PictureGeoExif als App gestartet wird (open PictureGeoExif.app), " +
                      "nicht aus dem Terminal oder per „dotnet run“. Dateien können weiterhin geöffnet werden.";
            return;
        }
        Status = await photos.RequestAuthorizationAsync();
        logger.LogInformation("Photo Library Authorization Status: {Status}", Status);
        if (Status.CanRead()) await OpenLibraryAsync();
        else Message = "Ohne Freigabe bleibt Apple Fotos gesperrt. Dateien können weiterhin geöffnet werden.";
    }

    [RelayCommand(CanExecute = nameof(CanOpenLibrary))]
    private async Task OpenLibraryAsync()
    {
        try
        {
            IsLoading = true;
            var albums = await photos.GetAlbumsAsync();
            Albums.Clear();
            foreach (var album in albums) Albums.Add(album);
            SelectedAlbum = Albums.FirstOrDefault();
            Message = Status == PhotoLibraryAccessStatus.Limited ? "Eingeschränkter Zugriff: nur freigegebene Fotos sind sichtbar." : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogError(ex, "Alben konnten nicht geladen werden.");
            Message = "Fotomediathek konnte nicht gelesen werden.";
        }
        finally { IsLoading = false; }
    }

    partial void OnSelectedAlbumChanged(PhotoAlbum? value) => _ = LoadAlbumAsync(value);

    private async Task LoadAlbumAsync(PhotoAlbum? album)
    {
        listing?.Cancel();
        listing = new CancellationTokenSource();
        var token = listing.Token;
        Assets.Clear();
        TotalCount = 0;
        if (album == null) return;
        provider = new ApplePhotosImageSourceProvider(photos, new PhotoQuery { AlbumId = album.Id }, album.Title);
        try
        {
            IsLoading = true;
            TotalCount = await provider.CountAsync(token);
            await LoadPageAsync(token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogError(ex, "Album konnte nicht geladen werden.");
            Message = "Album konnte nicht geladen werden.";
        }
        finally { if (!token.IsCancellationRequested) IsLoading = false; }
    }

    [RelayCommand(CanExecute = nameof(HasMore))]
    private async Task LoadMoreAsync()
    {
        if (listing == null) return;
        try
        {
            IsLoading = true;
            await LoadPageAsync(listing.Token);
        }
        catch (OperationCanceledException) { }
        finally { IsLoading = false; }
    }

    private async Task LoadPageAsync(CancellationToken token)
    {
        if (provider == null) return;
        var page = await provider.GetPageAsync(Assets.Count, PageSize, token);
        token.ThrowIfCancellationRequested();
        var entries = page.Select(item => new ImageEntryViewModel(item)).ToList();
        foreach (var entry in entries) Assets.Add(entry);
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(CountText));
        LoadMoreCommand.NotifyCanExecuteChanged();
        _ = LoadThumbnailsAsync(entries, token);
    }

    /// <summary>Thumbnails in the background; the service limits parallel PhotoKit requests.</summary>
    private async Task LoadThumbnailsAsync(IReadOnlyList<ImageEntryViewModel> entries, CancellationToken token)
    {
        foreach (var chunk in entries.Chunk(8))
        {
            if (token.IsCancellationRequested) return;
            var results = await Task.WhenAll(chunk.Select(async entry =>
            {
                try
                {
                    var bytes = await photos.GetThumbnailAsync(entry.Source.PhotoKitAssetId!, ThumbnailSize, token);
                    return (entry, bitmap: bytes == null ? null : await Task.Run(() => new Bitmap(new MemoryStream(bytes)), token));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    if (ex is not OperationCanceledException) logger.LogDebug(ex, "Thumbnail fehlgeschlagen.");
                    return (entry, bitmap: (Bitmap?)null);
                }
            }));
            foreach (var (entry, bitmap) in results) if (bitmap != null) entry.Thumbnail = bitmap;
        }
    }

    public void Cancel() => listing?.Cancel();

    public ImageSourceItem? Find(string assetId) => Assets.FirstOrDefault(a => a.Source.PhotoKitAssetId == assetId)?.Source;
}
