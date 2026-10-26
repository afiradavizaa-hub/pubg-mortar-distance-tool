using PubgMapDistanceTool.Core.Geometry;
using PubgMapDistanceTool.Core.Measurements;

namespace PubgMapDistanceTool.Core.CalibrationModel;

public sealed class Calibration
{
    public const double BenchmarkDistanceMeters = 600d;
    public const double MinimumPixelLength = 0.000001d;

    public Calibration(MapPoint left, MapPoint right, string coordinateSpaceId)
    {
        if (string.IsNullOrWhiteSpace(coordinateSpaceId))
        {
            throw new ArgumentException("坐标空间标识不能为空。", nameof(coordinateSpaceId));
        }

        if (!left.IsFinite || !right.IsFinite)
        {
            throw new ArgumentException("基准尺端点包含非法坐标。");
        }

        if (Math.Abs(left.Y - right.Y) > 0.001)
        {
            throw new ArgumentException("600 米基准尺必须保持水平。");
        }

        Left = left;
        Right = right;
        CoordinateSpaceId = coordinateSpaceId;

        if (PixelLength <= MinimumPixelLength)
        {
            throw new ArgumentException("基准尺像素长度必须大于零。");
        }
    }

    public MapPoint Left { get; }

    public MapPoint Right { get; }

    public string CoordinateSpaceId { get; }

    public double BenchmarkMeters => BenchmarkDistanceMeters;

    public double PixelLength => Left.DistanceTo(Right);

    public double MetersPerPixel => BenchmarkDistanceMeters / PixelLength;

    public void EnsureMatchesCoordinateSpace(string coordinateSpaceId)
    {
        if (!string.Equals(CoordinateSpaceId, coordinateSpaceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("校准比例与当前游戏坐标空间不匹配，请重新校准。");
        }
    }

    public MeasurementResult Measure(MapPoint start, MapPoint end)
    {
        if (!start.IsFinite || !end.IsFinite)
        {
            throw new ArgumentException("测量点包含非法坐标。");
        }

        var scale = MetersPerPixel;
        var horizontal = Math.Abs(end.X - start.X) * scale;
        var vertical = Math.Abs(end.Y - start.Y) * scale;
        var straight = start.DistanceTo(end) * scale;

        return new MeasurementResult(straight, horizontal, vertical);
    }
}
