using PubgMapDistanceTool.Core.CalibrationModel;
using PubgMapDistanceTool.Core.Geometry;

namespace PubgMapDistanceTool.Core.Measurements;

public sealed record MeasurementRecord(
    int Number,
    MapPoint Start,
    MapPoint End)
{
    public MeasurementResult Calculate(Calibration calibration) =>
        calibration.Measure(Start, End);

    public MeasurementRecord MoveStart(MapPoint point) => this with { Start = point };

    public MeasurementRecord MoveEnd(MapPoint point) => this with { End = point };
}
