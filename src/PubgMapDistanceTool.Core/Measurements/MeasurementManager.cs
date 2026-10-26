using PubgMapDistanceTool.Core.CalibrationModel;
using PubgMapDistanceTool.Core.Geometry;

namespace PubgMapDistanceTool.Core.Measurements;

public sealed class MeasurementManager
{
    private const int UndoLimit = 100;
    private readonly List<MeasurementRecord> _records = [];
    private readonly Stack<UndoState> _undo = [];
    private int _nextNumber = 1;

    public string? CoordinateSpaceId { get; private set; }

    public Calibration? Calibration { get; private set; }

    public IReadOnlyList<MeasurementRecord> Records => _records;

    public bool CanUndo => _undo.Count > 0;

    public bool CanMeasure =>
        CoordinateSpaceId is not null &&
        Calibration is { } calibration &&
        calibration.CoordinateSpaceId == CoordinateSpaceId;

    public void SetCoordinateSpace(string coordinateSpaceId)
    {
        if (string.IsNullOrWhiteSpace(coordinateSpaceId))
        {
            throw new ArgumentException("坐标空间标识不能为空。", nameof(coordinateSpaceId));
        }

        CoordinateSpaceId = coordinateSpaceId;
        Calibration = null;
        _records.Clear();
        _undo.Clear();
        _nextNumber = 1;
    }

    public void ClearCoordinateSpace()
    {
        CoordinateSpaceId = null;
        Calibration = null;
        _records.Clear();
        _undo.Clear();
        _nextNumber = 1;
    }

    public void ConfirmCalibration(Calibration calibration)
    {
        EnsureCoordinateSpace(calibration.CoordinateSpaceId);
        PushUndo();
        Calibration = calibration;
    }

    public MeasurementRecord AddMeasurement(MapPoint start, MapPoint end)
    {
        EnsureCanMeasure();
        if (!start.IsFinite || !end.IsFinite)
        {
            throw new ArgumentException("测量点包含非法坐标。");
        }

        if (start.DistanceTo(end) <= double.Epsilon)
        {
            throw new ArgumentException("起点和终点不能重合。");
        }

        PushUndo();
        _records.Clear();
        _nextNumber = 1;
        var record = new MeasurementRecord(_nextNumber++, start, end);
        _records.Add(record);
        return record;
    }

    public void UpdateMeasurementPoints(int number, MapPoint start, MapPoint end)
    {
        EnsureCanMeasure();
        var index = FindIndex(number);
        if (start.DistanceTo(end) <= double.Epsilon)
        {
            throw new ArgumentException("起点和终点不能重合。");
        }

        var current = _records[index];
        if (current.Start == start && current.End == end)
        {
            return;
        }

        PushUndo();
        _records[index] = current with { Start = start, End = end };
    }

    public bool DeleteMeasurement(int number)
    {
        var index = _records.FindIndex(item => item.Number == number);
        if (index < 0)
        {
            return false;
        }

        PushUndo();
        _records.RemoveAt(index);
        return true;
    }

    public bool ClearMeasurements()
    {
        if (_records.Count == 0)
        {
            return false;
        }

        PushUndo();
        _records.Clear();
        return true;
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        var state = _undo.Pop();
        CoordinateSpaceId = state.CoordinateSpaceId;
        Calibration = state.Calibration;
        _records.Clear();
        _records.AddRange(state.Records);
        _nextNumber = state.NextNumber;
        return true;
    }

    public MeasurementRecord? Find(int number) =>
        _records.Find(item => item.Number == number);

    private void EnsureCanMeasure()
    {
        if (CoordinateSpaceId is null)
        {
            throw new InvalidOperationException("游戏坐标空间尚未就绪。");
        }

        if (Calibration is null)
        {
            throw new InvalidOperationException("请先完成 600 米基准尺校准。");
        }

        Calibration.EnsureMatchesCoordinateSpace(CoordinateSpaceId);
    }

    private void EnsureCoordinateSpace(string coordinateSpaceId)
    {
        if (CoordinateSpaceId is null)
        {
            throw new InvalidOperationException("游戏坐标空间尚未就绪。");
        }

        if (!string.Equals(CoordinateSpaceId, coordinateSpaceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("校准数据与当前游戏坐标空间不匹配。");
        }
    }

    private int FindIndex(int number)
    {
        var index = _records.FindIndex(item => item.Number == number);
        return index >= 0 ? index : throw new ArgumentOutOfRangeException(nameof(number), "找不到指定的测量记录。");
    }

    private void PushUndo()
    {
        _undo.Push(new UndoState(
            CoordinateSpaceId,
            Calibration,
            [.. _records],
            _nextNumber));

        if (_undo.Count <= UndoLimit)
        {
            return;
        }

        var kept = _undo.Take(UndoLimit).Reverse().ToArray();
        _undo.Clear();
        foreach (var item in kept)
        {
            _undo.Push(item);
        }
    }

    private sealed record UndoState(
        string? CoordinateSpaceId,
        Calibration? Calibration,
        MeasurementRecord[] Records,
        int NextNumber);
}
