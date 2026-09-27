using System.Globalization;

namespace PictureGeoExif.Core.Geo;

/// <summary>WGS84 position in decimal degrees. Construction validates the range, so an instance is always usable.</summary>
public readonly record struct GeoCoordinate
{
    public double Latitude { get; }
    public double Longitude { get; }

    public GeoCoordinate(double latitude, double longitude)
    {
        if (!GpsValidation.IsValid(latitude, longitude)) throw new ArgumentOutOfRangeException(nameof(latitude), "Ungültige GPS-Koordinaten.");
        Latitude = latitude; Longitude = longitude;
    }

    public static GeoCoordinate? TryCreate(double? latitude, double? longitude) =>
        latitude is { } lat && longitude is { } lon && GpsValidation.IsValid(lat, lon) ? new GeoCoordinate(lat, lon) : null;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Latitude:F6}, {Longitude:F6}");
}

public static class GpsValidation
{
    /// <summary>Finite values within ±90 / ±180. Shared by every GPS write and every map/route input.</summary>
    public static bool IsValid(double latitude, double longitude) =>
        double.IsFinite(latitude) && double.IsFinite(longitude) && Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180;
}
