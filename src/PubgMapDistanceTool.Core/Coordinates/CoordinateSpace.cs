using PubgMapDistanceTool.Core.Geometry;

namespace PubgMapDistanceTool.Core.Coordinates;

public enum CoordinateSpaceKind
{
    GameClient,
    DesktopFallback
}

public readonly record struct PixelRect(double X, double Y, double Width, double Height)
{
    public bool IsValid =>
        double.IsFinite(X) &&
        double.IsFinite(Y) &&
        double.IsFinite(Width) &&
        double.IsFinite(Height) &&
        Width > 0 &&
        Height > 0;

    public bool Contains(PixelPoint point) =>
        point.X >= X &&
        point.Y >= Y &&
        point.X <= X + Width &&
        point.Y <= Y + Height;
}

public readonly record struct PixelPoint(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

public sealed record CoordinateSpace(
    string Id,
    PixelRect Bounds,
    CoordinateSpaceKind Kind,
    string DisplayName)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Id) &&
        Bounds.IsValid;
}
