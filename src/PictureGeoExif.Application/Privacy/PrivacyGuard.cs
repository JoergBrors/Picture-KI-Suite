using System.Globalization;
using PictureGeoExif.Core.Geo;

namespace PictureGeoExif.Application.Privacy;

/// <summary>What must never leave the machine in an AI request.</summary>
public sealed record PrivacyContext(IReadOnlyCollection<string> LocalPaths, IReadOnlyCollection<string> PhotoKitIds, IReadOnlyCollection<GeoCoordinate> ExactCoordinates)
{
    public static readonly PrivacyContext Empty = new([], [], []);
}

public sealed class PrivacyViolationException(string message) : Exception(message);

/// <summary>
/// Last line of defence before any text goes to OpenAI, Azure OpenAI or Gemini: rejects payloads that contain
/// file paths, PhotoKit identifiers or exact GPS coordinates. Applies to files and Photos assets alike.
/// </summary>
public static class PrivacyGuard
{
    public static void EnsureSafe(string payload, PrivacyContext context)
    {
        if (string.IsNullOrEmpty(payload)) return;
        foreach (var path in context.LocalPaths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            // Separator-agnostic: a macOS path must be recognised on Windows too (and vice versa).
            int cut = path.LastIndexOfAny(['/', '\\']);
            string? folder = cut > 0 ? path[..cut] : null;
            if (payload.Contains(path, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(folder) && folder.Length > 3 && payload.Contains(folder, StringComparison.OrdinalIgnoreCase)))
                throw new PrivacyViolationException("Anfrage enthält einen lokalen Dateipfad und wurde nicht gesendet.");
        }
        foreach (var id in context.PhotoKitIds.Where(i => !string.IsNullOrWhiteSpace(i)))
            if (payload.Contains(id, StringComparison.OrdinalIgnoreCase) || payload.Contains(id.Split('/')[0], StringComparison.OrdinalIgnoreCase))
                throw new PrivacyViolationException("Anfrage enthält eine Apple-Fotos-Kennung und wurde nicht gesendet.");
        foreach (var gps in context.ExactCoordinates)
            if (ContainsCoordinate(payload, gps.Latitude) && ContainsCoordinate(payload, gps.Longitude))
                throw new PrivacyViolationException("Anfrage enthält exakte GPS-Koordinaten und wurde nicht gesendet.");
    }

    /// <summary>A coordinate counts as present when it appears with three or more decimals (≈100 m) in dot or comma notation.</summary>
    private static bool ContainsCoordinate(string payload, double value)
    {
        // Truncate (not round) so the text is a prefix of every longer rendering of the same value.
        string text = (Math.Truncate(Math.Abs(value) * 1000 + 1e-7) / 1000).ToString("0.000", CultureInfo.InvariantCulture);
        return payload.Contains(text, StringComparison.Ordinal) || payload.Contains(text.Replace('.', ','), StringComparison.Ordinal);
    }
}
