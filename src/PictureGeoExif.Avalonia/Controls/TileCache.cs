using System.Net;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PictureGeoExif.Application.Maps;
using PictureGeoExif.Core;

namespace PictureGeoExif.Desktop.Controls;

/// <summary>
/// Loads slippy-map tiles with an honest user agent, at most two parallel connections, a 7-day disk cache and an
/// in-memory LRU (OpenStreetMap tile usage policy). Stops requesting for a while after HTTP 403/429.
/// </summary>
public sealed class TileCache : IDisposable
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly TimeSpan DiskLifetime = TimeSpan.FromDays(7);
    private const int MemoryTiles = 384;

    private readonly string template;
    private readonly string folder;
    private readonly SemaphoreSlim connections = new(2);
    private readonly Dictionary<(int Z, int X, int Y), Bitmap> memory = [];
    private readonly LinkedList<(int Z, int X, int Y)> order = new();
    private readonly HashSet<(int Z, int X, int Y)> pending = [];
    private readonly Lock gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private DateTime blockedUntil = DateTime.MinValue;

    public TileCache(string urlTemplate, string cacheRoot)
    {
        template = urlTemplate;
        string provider = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(urlTemplate)))[..12].ToLowerInvariant();
        folder = Path.Combine(cacheRoot, provider);
    }

    /// <summary>Raised on the UI thread when a tile became available.</summary>
    public event EventHandler? TileLoaded;
    /// <summary>Raised on the UI thread with a user-facing message (e.g. server refused requests).</summary>
    public event EventHandler<string>? StatusChanged;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 2, AutomaticDecompression = DecompressionMethods.All })
        { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
        return client;
    }

    /// <summary>Returns the tile if available, otherwise schedules loading and returns null.</summary>
    public Bitmap? Get(int z, int x, int y)
    {
        var key = (z, x, y);
        lock (gate)
        {
            if (memory.TryGetValue(key, out var bitmap))
            {
                order.Remove(key); order.AddFirst(key);
                return bitmap;
            }
            if (pending.Contains(key) || DateTime.UtcNow < blockedUntil) return null;
            pending.Add(key);
        }
        _ = LoadAsync(key);
        return null;
    }

    /// <summary>Returns a tile only if it is already in memory (used for low-zoom fallbacks while loading).</summary>
    public Bitmap? Peek(int z, int x, int y)
    {
        lock (gate) return memory.TryGetValue((z, x, y), out var bitmap) ? bitmap : null;
    }

    private async Task LoadAsync((int Z, int X, int Y) key)
    {
        var token = lifetime.Token;
        try
        {
            string file = Path.Combine(folder, key.Z.ToString(), key.X.ToString(), key.Y + ".png");
            byte[]? bytes = null;
            if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < DiskLifetime)
                bytes = await File.ReadAllBytesAsync(file, token);
            if (bytes == null)
            {
                await connections.WaitAsync(token);
                try
                {
                    if (DateTime.UtcNow < blockedUntil) return;
                    using var response = await Http.GetAsync(MapProjection.TileUri(template, key.Z, key.X, key.Y), token);
                    if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                    {
                        blockedUntil = DateTime.UtcNow.AddMinutes(1);
                        Post(() => StatusChanged?.Invoke(this, $"Kartenserver: HTTP {(int)response.StatusCode}. Bitte später erneut versuchen oder Kartenanbieter in settings.json wechseln."));
                        return;
                    }
                    if (!response.IsSuccessStatusCode) return;
                    bytes = await response.Content.ReadAsByteArrayAsync(token);
                }
                finally { connections.Release(); }
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    await File.WriteAllBytesAsync(file, bytes, token);
                }
                catch (IOException) { /* cache is optional */ }
                catch (UnauthorizedAccessException) { }
            }
            var bitmap = new Bitmap(new MemoryStream(bytes));
            lock (gate)
            {
                memory[key] = bitmap;
                order.AddFirst(key);
                while (order.Count > MemoryTiles)
                {
                    var oldest = order.Last!.Value;
                    order.RemoveLast();
                    // Bitmaps may still be referenced by a render pass; let the GC finalize them.
                    memory.Remove(oldest);
                }
            }
            Post(() => TileLoaded?.Invoke(this, EventArgs.Empty));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException or ArgumentException or InvalidOperationException)
        {
            Post(() => StatusChanged?.Invoke(this, "Karte offline oder Kachel nicht ladbar."));
        }
        finally
        {
            lock (gate) pending.Remove(key);
        }
    }

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);

    public void Dispose()
    {
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
