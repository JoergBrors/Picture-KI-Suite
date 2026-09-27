using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PictureGeoExif.Application.Maps;
using PictureGeoExif.Core.Geo;
using PictureGeoExif.Desktop.ViewModels;

namespace PictureGeoExif.Desktop.Controls;

/// <summary>
/// OpenStreetMap view without WebView: renders tiles, image markers, the selected and the picked coordinate,
/// virtual routes, road-matched segments and an optional meter grid. Drag pans, the wheel zooms, a click picks a
/// coordinate or selects a marker. Replaces the WebView2/Leaflet map of the WPF version.
/// </summary>
public sealed class OsmMapControl : Control
{
    public static readonly StyledProperty<MapViewModel?> MapProperty =
        AvaloniaProperty.Register<OsmMapControl, MapViewModel?>(nameof(Map));

    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
    private static readonly IPen TrunkPen = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0xE0, 0x6C, 0x00)), 4);
    private static readonly IPen BranchPen = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0xE0, 0x6C, 0x00)), 2.5, new DashStyle([3, 2], 0));
    private static readonly IPen RoadPen = new Pen(new SolidColorBrush(Color.FromArgb(0xDD, 0x00, 0x7A, 0xCC)), 4);
    private static readonly IPen RoadBranchPen = new Pen(new SolidColorBrush(Color.FromArgb(0xDD, 0x00, 0x7A, 0xCC)), 2.5);
    private static readonly IPen OffRoadPen = new Pen(new SolidColorBrush(Color.FromArgb(0xDD, 0xC0, 0x00, 0x30)), 2.5, new DashStyle([2, 2], 0));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0x33, 0x33, 0x33)), 1);
    private static readonly IPen MarkerPen = new Pen(Brushes.White, 1.5);
    private static readonly IBrush MarkerBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5));
    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    private static readonly IPen CurrentPen = new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)), 3);
    private static readonly Typeface Font = new("Inter");

    private TileCache? tiles;
    private MapViewModel? subscribed;
    private int zoom = 6;
    private GeoCoordinate center = new(51.1657, 10.4515);
    private Point? pressPoint;
    private (double X, double Y) pressCenterPixel;
    private bool dragging;
    private bool pendingFit;

    static OsmMapControl()
    {
        AffectsRender<OsmMapControl>(MapProperty);
        FocusableProperty.OverrideDefaultValue<OsmMapControl>(true);
    }

    public OsmMapControl() => ClipToBounds = true;

    public MapViewModel? Map
    {
        get => GetValue(MapProperty);
        set => SetValue(MapProperty, value);
    }

    public int Zoom => zoom;
    public GeoCoordinate Center => center;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MapProperty) Subscribe(change.GetNewValue<MapViewModel?>());
    }

    private void Subscribe(MapViewModel? map)
    {
        if (subscribed != null)
        {
            subscribed.PropertyChanged -= OnMapChanged;
            subscribed.FitRequested -= OnFitRequested;
            subscribed.CenterRequested -= OnCenterRequested;
        }
        tiles?.Dispose();
        tiles = null;
        subscribed = map;
        if (map == null) return;
        map.PropertyChanged += OnMapChanged;
        map.FitRequested += OnFitRequested;
        map.CenterRequested += OnCenterRequested;
        tiles = new TileCache(map.TileUrl, map.TileCacheFolder);
        tiles.TileLoaded += (_, _) => InvalidateVisual();
        tiles.StatusChanged += (_, text) => { if (subscribed != null) subscribed.Status = text; };
        InvalidateVisual();
    }

    private void OnMapChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private void OnFitRequested(object? sender, EventArgs e)
    {
        if (Bounds.Width < 10 || Bounds.Height < 10) { pendingFit = true; return; }
        FitToMarkers();
    }

    private void OnCenterRequested(object? sender, GeoCoordinate position)
    {
        center = position;
        if (zoom < 12) zoom = 15;
        InvalidateVisual();
    }

    public void FitToMarkers()
    {
        pendingFit = false;
        var points = Map?.Markers.Select(m => m.Position).ToList() ?? [];
        if (points.Count == 0) return;
        zoom = MapProjection.FitZoom(points, Bounds.Width, Bounds.Height);
        center = MapProjection.Center(points);
        InvalidateVisual();
    }

    public void ZoomBy(int delta, Point? around = null)
    {
        int next = Math.Clamp(zoom + delta, MapProjection.MinZoom, MapProjection.MaxZoom);
        if (next == zoom) return;
        var anchor = around ?? new Point(Bounds.Width / 2, Bounds.Height / 2);
        var anchorGeo = ToCoordinate(anchor);
        zoom = next;
        // Keep the geographic point under the cursor fixed.
        var anchorPixel = MapProjection.ToPixel(anchorGeo.Latitude, anchorGeo.Longitude, zoom);
        var centerPixel = (X: anchorPixel.X - (anchor.X - Bounds.Width / 2), Y: anchorPixel.Y - (anchor.Y - Bounds.Height / 2));
        center = MapProjection.ToCoordinate(centerPixel.X, centerPixel.Y, zoom);
        InvalidateVisual();
    }

    // ---------------- Coordinates ----------------

    private (double X, double Y) CenterPixel => MapProjection.ToPixel(center.Latitude, center.Longitude, zoom);

    private Point ToScreen(GeoCoordinate position)
    {
        var (x, y) = MapProjection.ToPixel(position.Latitude, position.Longitude, zoom);
        var (cx, cy) = CenterPixel;
        return new Point(x - cx + Bounds.Width / 2, y - cy + Bounds.Height / 2);
    }

    private GeoCoordinate ToCoordinate(Point screen)
    {
        var (cx, cy) = CenterPixel;
        double world = MapProjection.WorldSize(zoom);
        double x = cx + screen.X - Bounds.Width / 2, y = Math.Clamp(cy + screen.Y - Bounds.Height / 2, 0, world);
        return MapProjection.ToCoordinate(x, y, zoom);
    }

    // ---------------- Rendering ----------------

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Background, bounds);
        if (pendingFit && bounds.Width > 10) FitToMarkers();
        var map = Map;
        if (map == null || tiles == null) return;

        using (context.PushClip(bounds))
        {
            DrawTiles(context, bounds);
            if (map.GridEnabled) DrawGrid(context, bounds, map.GridMeters);
            if (map.ShowRoutes) foreach (var line in map.Routes) DrawLine(context, line);
            if (map.ShowRoad) foreach (var line in map.RoadSegments) DrawLine(context, line);
            if (map.ShowImages)
                foreach (var marker in map.Markers.Where(m => m.Id != map.SelectedMarkerId))
                    context.DrawEllipse(MarkerBrush, MarkerPen, ToScreen(marker.Position), 6, 6);
            if (map.SelectedPosition is { } selected) context.DrawEllipse(SelectedBrush, MarkerPen, ToScreen(selected), 8, 8);
            if (map.CurrentPosition is { } current)
            {
                var p = ToScreen(current);
                context.DrawLine(CurrentPen, p + new Point(-10, 0), p + new Point(10, 0));
                context.DrawLine(CurrentPen, p + new Point(0, -10), p + new Point(0, 10));
                context.DrawEllipse(null, CurrentPen, p, 6, 6);
            }
            DrawAttribution(context, bounds, map.Attribution);
        }
    }

    private void DrawTiles(DrawingContext context, Rect bounds)
    {
        var (cx, cy) = CenterPixel;
        double left = cx - bounds.Width / 2, top = cy - bounds.Height / 2;
        int count = 1 << zoom;
        int firstX = (int)Math.Floor(left / MapProjection.TileSize), lastX = (int)Math.Floor((left + bounds.Width) / MapProjection.TileSize);
        int firstY = Math.Max(0, (int)Math.Floor(top / MapProjection.TileSize)), lastY = Math.Min(count - 1, (int)Math.Floor((top + bounds.Height) / MapProjection.TileSize));
        for (int ty = firstY; ty <= lastY; ty++)
        for (int tx = firstX; tx <= lastX; tx++)
        {
            int wrapped = ((tx % count) + count) % count;
            var dest = new Rect(tx * MapProjection.TileSize - left, ty * MapProjection.TileSize - top, MapProjection.TileSize, MapProjection.TileSize);
            var bitmap = tiles!.Get(zoom, wrapped, ty);
            if (bitmap != null) { context.DrawImage(bitmap, new Rect(bitmap.Size), dest); continue; }
            // While loading, stretch the parent tile if it is already in memory.
            if (zoom > 0 && tiles.Peek(zoom - 1, wrapped / 2, ty / 2) is { } parent)
            {
                double half = parent.Size.Width / 2;
                var source = new Rect(wrapped % 2 * half, ty % 2 * half, half, half);
                context.DrawImage(parent, source, dest);
            }
        }
    }

    private void DrawGrid(DrawingContext context, Rect bounds, double meters)
    {
        double metersPerPixel = MapProjection.MetersPerPixel(center.Latitude, zoom);
        double spacing = meters / metersPerPixel;
        if (spacing < 8) return; // too dense to be useful
        var (cx, cy) = CenterPixel;
        double left = cx - bounds.Width / 2, top = cy - bounds.Height / 2;
        for (double x = spacing - left % spacing; x < bounds.Width; x += spacing) context.DrawLine(GridPen, new Point(x, 0), new Point(x, bounds.Height));
        for (double y = spacing - top % spacing; y < bounds.Height; y += spacing) context.DrawLine(GridPen, new Point(0, y), new Point(bounds.Width, y));
    }

    private void DrawLine(DrawingContext context, MapPolyline line)
    {
        if (line.Points.Count < 2) return;
        var pen = line.Style switch
        {
            MapLineStyle.RouteTrunk => TrunkPen,
            MapLineStyle.RouteBranch => BranchPen,
            MapLineStyle.RoadOnRoad => RoadPen,
            MapLineStyle.RoadBranch => RoadBranchPen,
            _ => OffRoadPen
        };
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(ToScreen(line.Points[0]), false);
            foreach (var point in line.Points.Skip(1)) g.LineTo(ToScreen(point));
            g.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }

    private static void DrawAttribution(DrawingContext context, Rect bounds, string text)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Font, 11, Brushes.Black);
        var box = new Rect(bounds.Width - formatted.Width - 10, bounds.Height - formatted.Height - 6, formatted.Width + 8, formatted.Height + 4);
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), box);
        context.DrawText(formatted, box.TopLeft + new Point(4, 2));
    }

    public bool IsOnAttribution(Point point) => point.X > Bounds.Width - 220 && point.Y > Bounds.Height - 22;

    // ---------------- Input ----------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        pressPoint = e.GetPosition(this);
        pressCenterPixel = CenterPixel;
        dragging = false;
        e.Pointer.Capture(this);
        Focus();
        if (e.ClickCount == 2) { ZoomBy(1, pressPoint); pressPoint = null; }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (pressPoint is not { } start) return;
        var now = e.GetPosition(this);
        var delta = now - start;
        if (!dragging && Math.Abs(delta.X) + Math.Abs(delta.Y) < 4) return;
        dragging = true;
        double world = MapProjection.WorldSize(zoom);
        double y = Math.Clamp(pressCenterPixel.Y - delta.Y, 0, world);
        center = MapProjection.ToCoordinate(pressCenterPixel.X - delta.X, y, zoom);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (pressPoint is not { } start) return;
        pressPoint = null;
        e.Pointer.Capture(null);
        if (dragging || Map is not { } map) return;
        if (IsOnAttribution(start)) return;
        // Marker hit test first (nearest within 10 px), otherwise pick a coordinate.
        var hit = map.ShowImages
            ? map.Markers.Select(m => (m, d: Distance(ToScreen(m.Position), start))).Where(x => x.d <= 10).OrderBy(x => x.d).Select(x => x.m).FirstOrDefault()
            : null;
        if (hit != null) map.OnMarkerClicked(hit.Id);
        else map.OnClicked(ToCoordinate(start));
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Math.Abs(e.Delta.Y) < 0.01) return;
        ZoomBy(e.Delta.Y > 0 ? 1 : -1, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Add or Key.OemPlus: ZoomBy(1); e.Handled = true; break;
            case Key.Subtract or Key.OemMinus: ZoomBy(-1); e.Handled = true; break;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        tiles?.Dispose();
        tiles = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (tiles == null && Map != null) Subscribe(Map);
    }
}
