using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PictureGeoExif.Application.Logging;

/// <summary>
/// Minimal rolling file logger (one file per day, 14 days kept). Messages are written as given; callers must not log
/// user content such as file paths, GPS positions, PhotoKit identifiers or API keys.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string folder;
    private readonly LogLevel minimum;
    private readonly BlockingCollection<string> queue = new(4096);
    private readonly Thread writer;

    public FileLoggerProvider(string folder, LogLevel minimum = LogLevel.Information)
    {
        this.folder = folder;
        this.minimum = minimum;
        Directory.CreateDirectory(folder);
        Cleanup();
        writer = new Thread(Pump) { IsBackground = true, Name = "PictureGeoExif log writer" };
        writer.Start();
    }

    public string CurrentFile => Path.Combine(folder, $"PictureGeoExif-{DateTime.Now:yyyyMMdd}.log");

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    private void Cleanup()
    {
        foreach (var file in Directory.EnumerateFiles(folder, "PictureGeoExif-*.log"))
        {
            try { if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14)) File.Delete(file); }
            catch (IOException) { }
        }
    }

    private void Pump()
    {
        foreach (var line in queue.GetConsumingEnumerable())
        {
            try { File.AppendAllText(CurrentFile, line, Encoding.UTF8); }
            catch (IOException) { /* logging must never break the app */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal void Enqueue(string line) => queue.TryAdd(line);

    public void Dispose()
    {
        queue.CompleteAdding();
        writer.Join(TimeSpan.FromSeconds(2));
        queue.Dispose();
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= owner.minimum && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var line = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)).Append(' ')
                .Append(logLevel.ToString().ToUpperInvariant()[..4]).Append(' ')
                .Append(category[(category.LastIndexOf('.') + 1)..]).Append(": ")
                .Append(formatter(state, exception));
            if (exception != null) line.Append(Environment.NewLine).Append(exception);
            owner.Enqueue(line.Append(Environment.NewLine).ToString());
        }
    }
}
