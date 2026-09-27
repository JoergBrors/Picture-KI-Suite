using System.Globalization;
using PictureGeoExif.Core.Geo;

namespace PictureGeoExif.Application.Maps;

/// <summary>Road match of one route: the trunk and each branch separately (branch requests start at the attach point).</summary>
public sealed record RouteRoadMatch(RoadMatch Trunk, IReadOnlyList<RoadMatch> Branches);

/// <summary>Converts virtual routes and road matches into map polylines; formerly JavaScript payloads for Leaflet.</summary>
public static class RouteLayers
{
    public static IReadOnlyList<MapPolyline> Routes(IEnumerable<Route> routes)
    {
        var lines = new List<MapPolyline>();
        foreach (var route in routes)
        {
            if (route.Trunk.Count >= 2)
                lines.Add(new MapPolyline(route.Trunk.Select(p => new GeoCoordinate(p.Latitude, p.Longitude)).ToList(), MapLineStyle.RouteTrunk, route.Number));
            foreach (var branch in route.Branches)
                lines.Add(new MapPolyline(branch.Points.Select(p => new GeoCoordinate(p.Latitude, p.Longitude))
                    .Prepend(new GeoCoordinate(branch.AttachLatitude, branch.AttachLongitude)).ToList(), MapLineStyle.RouteBranch, route.Number));
        }
        return lines;
    }

    public static IReadOnlyList<RoutePoint> BranchRequest(RouteBranch branch) =>
        branch.Points.Prepend(new RoutePoint(-1, branch.AttachLatitude, branch.AttachLongitude)).ToList();

    private static string Js(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Coordinates(IEnumerable<(double Lat, double Lon)> points) =>
        string.Join(";", points.Select(p => $"{Js(p.Lat)},{Js(p.Lon)}"));

    /// <summary>Cache key: exact trunk/branch geometry plus all matching settings, so any change needs a new explicit request.</summary>
    public static string RoadKey(Route route, AppSettings settings) =>
        string.Join("|", settings.RoadMatchUrl, settings.RoadMatchProfile, settings.RoadMatchMaxDeviationMeters.ToString(CultureInfo.InvariantCulture),
            "T:" + Coordinates(route.Trunk.Select(p => (p.Latitude, p.Longitude))),
            string.Join("", route.Branches.Select(b => "B:" + Coordinates(b.Points.Select(p => (p.Latitude, p.Longitude))
                .Prepend((b.AttachLatitude, b.AttachLongitude))))));

    /// <summary>Road segments of the cached matches and the distance of each photo (by route point id) to the nearest way.</summary>
    public static (IReadOnlyList<MapPolyline> Segments, IReadOnlyDictionary<RoutePoint, double?> Distances) Road(
        IEnumerable<Route> routes, IReadOnlyDictionary<string, RouteRoadMatch> cache, AppSettings settings)
    {
        var lines = new List<MapPolyline>();
        var distances = new Dictionary<RoutePoint, double?>();
        foreach (var route in routes)
        {
            if (!cache.TryGetValue(RoadKey(route, settings), out var match)) continue;
            for (int k = 0; k < route.Trunk.Count && k < match.Trunk.Deviations.Count; k++)
                distances[route.Trunk[k]] = match.Trunk.Deviations[k];
            lines.AddRange(match.Trunk.Segments.Where(g => g.Points.Count >= 2).Select(g => Line(g.Points, g.OnRoad ? MapLineStyle.RoadOnRoad : MapLineStyle.RoadOffRoad, route.Number)));

            for (int b = 0; b < route.Branches.Count && b < match.Branches.Count; b++)
            {
                var branch = route.Branches[b];
                var branchMatch = match.Branches[b];
                // Deviation index 0 is the attach point on the trunk, not a photo.
                for (int k = 0; k < branch.Points.Count && k + 1 < branchMatch.Deviations.Count; k++)
                    distances[branch.Points[k]] = branchMatch.Deviations[k + 1];
                lines.AddRange(branchMatch.Segments.Where(g => g.Points.Count >= 2).Select(g => Line(g.Points, g.OnRoad ? MapLineStyle.RoadBranch : MapLineStyle.RoadOffRoad, route.Number)));
                // A connection ends at the photo (e.g. inside the property), not where the road does.
                var end = branchMatch.Segments.LastOrDefault()?.Points[^1];
                var photo = (branch.Points[^1].Latitude, branch.Points[^1].Longitude);
                if (end is { } last && last != photo) lines.Add(Line([last, photo], MapLineStyle.RoadOffRoad, route.Number));
            }
        }
        return (lines, distances);

        static MapPolyline Line(IEnumerable<(double Lat, double Lon)> points, MapLineStyle style, int number) =>
            new(points.Where(p => GpsValidation.IsValid(p.Lat, p.Lon)).Select(p => new GeoCoordinate(p.Lat, p.Lon)).ToList(), style, number);
    }
}
