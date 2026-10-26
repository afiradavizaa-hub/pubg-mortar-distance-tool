namespace PubgMapDistanceTool.Core.Measurements;

public readonly record struct MeasurementResult(
    double StraightMeters,
    double HorizontalMeters,
    double VerticalMeters);
