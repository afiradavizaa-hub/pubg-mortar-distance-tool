using PubgMapDistanceTool.Core.Geometry;

namespace PubgMapDistanceTool.Core.Coordinates;

public sealed class UnifiedCoordinateMap
{
    public UnifiedCoordinateMap(
        PixelRect coordinateSpaceBounds,
        PixelRect overlayClientBounds,
        double dpiScaleX,
        double dpiScaleY)
    {
        if (!coordinateSpaceBounds.IsValid)
        {
            throw new ArgumentException("游戏坐标空间边界无效。", nameof(coordinateSpaceBounds));
        }

        if (!overlayClientBounds.IsValid)
        {
            throw new ArgumentException("悬浮窗客户区边界无效。", nameof(overlayClientBounds));
        }

        if (!double.IsFinite(dpiScaleX) || !double.IsFinite(dpiScaleY) ||
            dpiScaleX <= 0 || dpiScaleY <= 0)
        {
            throw new ArgumentException("DPI 缩放比例无效。");
        }

        CoordinateSpaceBounds = coordinateSpaceBounds;
        OverlayClientBounds = overlayClientBounds;
        DpiScaleX = dpiScaleX;
        DpiScaleY = dpiScaleY;
    }

    public PixelRect CoordinateSpaceBounds { get; }

    public PixelRect OverlayClientBounds { get; }

    public double DpiScaleX { get; }

    public double DpiScaleY { get; }

    public PixelPoint CoordinateToScreen(MapPoint point) =>
        new(CoordinateSpaceBounds.X + point.X, CoordinateSpaceBounds.Y + point.Y);

    public MapPoint ScreenToCoordinate(PixelPoint point) =>
        new(point.X - CoordinateSpaceBounds.X, point.Y - CoordinateSpaceBounds.Y);

    public ScreenPoint OverlayDipToScreen(ScreenPoint overlayDip) =>
        new(
            OverlayClientBounds.X + (overlayDip.X * DpiScaleX),
            OverlayClientBounds.Y + (overlayDip.Y * DpiScaleY));

    public ScreenPoint ScreenToOverlayDip(ScreenPoint screen) =>
        new(
            (screen.X - OverlayClientBounds.X) / DpiScaleX,
            (screen.Y - OverlayClientBounds.Y) / DpiScaleY);

    public ScreenPoint CoordinateToOverlayDip(MapPoint point)
    {
        var screen = CoordinateToScreen(point);
        return ScreenToOverlayDip(new ScreenPoint(screen.X, screen.Y));
    }

    public MapPoint OverlayDipToCoordinate(ScreenPoint overlayDip)
    {
        var screen = OverlayDipToScreen(overlayDip);
        return ScreenToCoordinate(new PixelPoint(screen.X, screen.Y));
    }

    public bool ContainsCoordinate(MapPoint point) =>
        point.IsFinite &&
        point.X >= 0 &&
        point.Y >= 0 &&
        point.X <= CoordinateSpaceBounds.Width &&
        point.Y <= CoordinateSpaceBounds.Height;

    public bool ContainsScreen(PixelPoint point) => CoordinateSpaceBounds.Contains(point);
}
