using System.Text.Json;
using PictureGeoExif.Core.Geo;
using PictureGeoExif.Core.IO;

namespace PictureGeoExif.Application
{
    public class AppSettings
    {
        public string TileUrl { get; set; } = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
        public string TileAttribution { get; set; } = "OpenStreetMap contributors";
        public string TileAttributionUrl { get; set; } = "https://www.openstreetmap.org/copyright";
        public string OutputFolder { get; set; } = string.Empty;
        /// <summary>Photos farther apart than this start a new virtual route (trench).</summary>
        public double RouteMaxGapMeters { get; set; } = 200;
        public bool SortImagesByRoute { get; set; } = true;
        /// <summary>Photos farther than this from the trunk line become branches (house connections); nearer ones count as GPS jitter.</summary>
        public double RouteBranchMinMeters { get; set; } = 10;
        /// <summary>Valhalla-compatible map-matching server. Default: public FOSSGIS demo server (fair use, max. 1 request/s).</summary>
        public string RoadMatchUrl { get; set; } = RoadMatcher.DefaultServer;
        /// <summary>Valhalla costing: pedestrian (paths, sidewalks), bicycle or auto.</summary>
        public string RoadMatchProfile { get; set; } = "pedestrian";
        /// <summary>Photos farther from the nearest way are connected in a straight line instead.</summary>
        public double RoadMatchMaxDeviationMeters { get; set; } = 25;
        /// <summary>Per provider profile; prices are entered and dated by the user, never hard-coded.</summary>
        public Dictionary<string, AiPriceEntry> AiPrices { get; set; } = new();
        public string? AiTemplatePath { get; set; }
        /// <summary>Show the meter grid on the map.</summary>
        public bool MapGridEnabled { get; set; }
        public double MapGridMeters { get; set; } = 100;
        
        /// <summary>Where this instance is persisted; tests use a temporary path so user settings are never touched.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string FilePath { get; init; } = DefaultPath;

        public static string DefaultPath => AppPaths.Current.SettingsFile;

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(DefaultPath))
                {
                    var json = File.ReadAllText(DefaultPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // Unreadable settings must never block the start; defaults are used instead.
            }
            
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                AtomicFile.Write(FilePath, System.Text.Encoding.UTF8.GetBytes(json));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Settings are a convenience; a failed save must not crash the app.
            }
        }
    }

    public sealed class AiPriceEntry
    {
        public decimal Input { get; set; }
        public decimal Cached { get; set; }
        public decimal Output { get; set; }
        public DateTime? VerifiedDate { get; set; }
    }
}
