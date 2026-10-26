using PubgMapDistanceTool.Core.Geometry;

namespace PubgMapDistanceTool.Controls;

public sealed class MeasurementCompletedEventArgs(MapPoint start, MapPoint end) : EventArgs
{
    public MapPoint Start { get; } = start;

    public MapPoint End { get; } = end;
}

public sealed class MeasurementPointChangedEventArgs(
    int number,
    bool isStart,
    MapPoint point) : EventArgs
{
    public int Number { get; } = number;

    public bool IsStart { get; } = isStart;

    public MapPoint Point { get; } = point;
}
