namespace PubgMapDistanceTool.Core.Geometry;

public readonly record struct MapPoint(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);

    public double DistanceTo(MapPoint other)
    {
        var dx = other.X - X;
        var dy = other.Y - Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public MapPoint Clamp(double width, double height) =>
        new(Math.Clamp(X, 0, width), Math.Clamp(Y, 0, height));
}

public readonly record struct ScreenPoint(double X, double Y);

public readonly record struct MapSize(double Width, double Height)
{
    public bool IsValid => double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0;
}
