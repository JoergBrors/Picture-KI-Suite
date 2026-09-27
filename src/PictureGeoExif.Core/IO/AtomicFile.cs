namespace PictureGeoExif.Core.IO;

/// <summary>
/// Crash-safe file writes: a temporary file in the target folder is flushed and then moved/replaced in one step,
/// so readers never see a partially written file. New export names never collide with existing files.
/// </summary>
public static class AtomicFile
{
    public static void Write(string path, byte[] bytes, bool overwrite = true)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            Commit(temporary, full, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Streams <paramref name="source"/> into a temporary file next to <paramref name="path"/> and commits it atomically.</summary>
    public static async Task WriteAsync(string path, Stream source, bool overwrite = true, CancellationToken token = default)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(stream, token);
                await stream.FlushAsync(token);
                stream.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            Commit(temporary, full, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Moves an already written temporary file into place (same volume), never overwriting unless asked.</summary>
    public static void Commit(string temporary, string target, bool overwrite)
    {
        if (overwrite && File.Exists(target)) File.Replace(temporary, target, null);
        else File.Move(temporary, target, overwrite: false);
    }

    /// <summary><c>folder/yyyyMMdd/name_yyyyMMdd_HHmmss_fff_GUID.ext</c>: unique, sortable, never an existing file.</summary>
    public static string ExportPath(string folder, string name)
    {
        var directory = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(name)}_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}{Path.GetExtension(name)}");
    }

    /// <summary>
    /// Returns <paramref name="path"/> if it does not exist, otherwise "name (2).ext", "name (3).ext" …
    /// Used for user-chosen export targets where a readable name matters more than a GUID.
    /// </summary>
    public static string FreePath(string path)
    {
        if (!File.Exists(path)) return path;
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))!, stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        for (int i = 2; i < 10_000; i++)
        {
            string candidate = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(folder, $"{stem}_{Guid.NewGuid():N}{ext}");
    }

    /// <summary>Removes characters that are invalid in file names on Windows or macOS; never returns an empty name.</summary>
    public static string SafeFileName(string? name, string fallback = "bild")
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        // Last segment after '/' or '\' on every OS; Path.GetFileName would also treat "a:" as a drive on Windows.
        string trimmed = name.Trim();
        string last = trimmed[(trimmed.LastIndexOfAny(['/', '\\']) + 1)..];
        var cleaned = new string(last.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim('.', ' ');
        return cleaned.Length == 0 ? fallback : cleaned;
    }
}
