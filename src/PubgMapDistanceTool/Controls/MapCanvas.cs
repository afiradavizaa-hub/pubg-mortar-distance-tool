using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PubgMapDistanceTool.Core.CalibrationModel;
using PubgMapDistanceTool.Core.Coordinates;
using PubgMapDistanceTool.Core.Geometry;
using PubgMapDistanceTool.Core.Measurements;
using PubgMapDistanceTool.Platform;
using ScreenPoint = PubgMapDistanceTool.Core.Geometry.ScreenPoint;

namespace PubgMapDistanceTool.Controls;

public enum MapInteractionMode
{
    View,
    Calibrate,
    Measure
}

public sealed class MapCanvas : FrameworkElement
{
    private const double EndpointHitRadius = 14d;
    private const double LineHitRadius = 9d;
    private readonly List<MeasurementRecord> _measurements = [];
    private CoordinateSpace? _coordinateSpace;
    private Calibration? _calibration;
    private MapPoint? _draftLeft;
    private MapPoint? _draftRight;
    private MapPoint? _rulerCreateStart;
    private MapPoint? _measureStart;
    private MapPoint? _previewPoint;
    private MapPoint? _dragMeasurementPoint;
    private Point _lastMousePosition;
    private Point _dragOriginPoint;
    private DragKind _dragKind;
    private bool _dragMoved;
    private bool _completeMeasurementOnUp;
    private int? _dragMeasurementNumber;
    private bool _dragMeasurementIsStart;

    public MapCanvas()
    {
        SnapsToDevicePixels = true;
        ClipToBounds = true;
        Cursor = Cursors.Arrow;
        Focusable = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    public event EventHandler<MeasurementCompletedEventArgs>? MeasurementCompleted;

    public event EventHandler<MeasurementPointChangedEventArgs>? MeasurementPointChanged;

    public event EventHandler<int>? MeasurementSelected;

    public event EventHandler? CalibrationDraftChanged;

    public MapInteractionMode Mode { get; private set; }

    public int? SelectedMeasurementNumber { get; set; }

    public CoordinateSpace? CoordinateSpace => _coordinateSpace;

    public Calibration? Calibration => _calibration;

    public MapPoint? DraftLeft => _draftLeft;

    public MapPoint? DraftRight => _draftRight;

    public IReadOnlyList<MeasurementRecord> Measurements => _measurements;

    public bool CanInteract { get; set; } = true;

    public double DraftPixelLength =>
        _draftLeft is { } left && _draftRight is { } right
            ? left.DistanceTo(right)
            : 0;

    public void SetCoordinateSpace(CoordinateSpace? space)
    {
        var oldId = _coordinateSpace?.Id;
        _coordinateSpace = space;
        if (!string.Equals(oldId, space?.Id, StringComparison.Ordinal))
        {
            _calibration = null;
            _measurements.Clear();
            _draftLeft = null;
            _draftRight = null;
            _rulerCreateStart = null;
            _measureStart = null;
            _previewPoint = null;
            SelectedMeasurementNumber = null;
            Mode = MapInteractionMode.View;
        }

        InvalidateVisual();
    }

    public void SetCalibration(Calibration? calibration)
    {
        _calibration = calibration;
        if (calibration is not null && Mode != MapInteractionMode.Calibrate)
        {
            _draftLeft = calibration.Left;
            _draftRight = calibration.Right;
        }

        InvalidateVisual();
    }

    public void SetMeasurements(IReadOnlyList<MeasurementRecord> measurements)
    {
        _measurements.Clear();
        _measurements.AddRange(measurements);
        InvalidateVisual();
    }

    public void EnterCalibration()
    {
        if (_coordinateSpace is not { IsValid: true })
        {
            return;
        }

        Mode = MapInteractionMode.Calibrate;
        _measureStart = null;
        _previewPoint = null;
        if (_calibration is not null)
        {
            SetHorizontalDraft(_calibration.Left, _calibration.Right);
        }
        else if (_draftLeft is null || _draftRight is null)
        {
            CreateDefaultRuler();
        }

        _rulerCreateStart = null;
        Cursor = Cursors.Hand;
        CalibrationDraftChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void EnterMeasurement()
    {
        if (_calibration is null)
        {
            return;
        }

        Mode = MapInteractionMode.Measure;
        _measureStart = null;
        _previewPoint = null;
        Cursor = Cursors.Cross;
        InvalidateVisual();
    }

    public void CancelCurrentMode()
    {
        Mode = MapInteractionMode.View;
        _measureStart = null;
        _previewPoint = null;
        _rulerCreateStart = null;
        if (_calibration is not null)
        {
            _draftLeft = _calibration.Left;
            _draftRight = _calibration.Right;
        }

        Cursor = Cursors.Arrow;
        InvalidateVisual();
    }

    public void CancelPendingMeasurement()
    {
        _measureStart = null;
        _previewPoint = null;
        InvalidateVisual();
    }

    public void RefreshDrawing() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_coordinateSpace is null)
        {
            return;
        }

        // A transparent drawing surface keeps the whole coordinate space hit-testable
        // without painting any visible background over the game.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        DrawRuler(dc);
        DrawMeasurements(dc);
        DrawMeasurementPreview(dc);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (!CanInteract || e.ChangedButton != MouseButton.Left || !TryGetCoordinateMap(out _))
        {
            return;
        }

        Focus();
        _lastMousePosition = e.GetPosition(this);
        var coordinate = CoordinateAt(_lastMousePosition);
        if (coordinate is null)
        {
            return;
        }

        switch (Mode)
        {
            case MapInteractionMode.Calibrate:
                HandleCalibrationMouseDown(coordinate.Value, _lastMousePosition);
                e.Handled = true;
                break;

            case MapInteractionMode.Measure:
                HandleMeasurementMouseDown(coordinate.Value);
                e.Handled = true;
                break;

            default:
                e.Handled = HandleViewMouseDown(coordinate.Value, _lastMousePosition);
                break;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!CanInteract || !TryGetCoordinateMap(out _))
        {
            return;
        }

        var position = e.GetPosition(this);
        if (_dragKind is DragKind.RulerLeft or DragKind.RulerRight or DragKind.RulerMove or DragKind.RulerCreate)
        {
            UpdateRulerDrag(position);
            return;
        }

        if (_dragKind is DragKind.MeasurementStart or DragKind.MeasurementEnd)
        {
            UpdateMeasurementDrag(position);
            return;
        }

        if (_dragKind == DragKind.MeasurementCreate)
        {
            UpdateMeasurementDraft(position);
            return;
        }

        if (_dragKind == DragKind.RulerCreate)
        {
            var end = CoordinateAt(e.GetPosition(this));
            if (_rulerCreateStart is { } start && end is { } finish && _dragMoved)
            {
                SetHorizontalDraft(start, finish);
                _rulerCreateStart = null;
                CalibrationDraftChanged?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
            }

            EndDrag();
            e.Handled = true;
            return;
        }

        if (Mode == MapInteractionMode.Measure && _measureStart is not null)
        {
            _previewPoint = CoordinateAt(position);
            InvalidateVisual();
        }

        UpdateHoverCursor(position);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragKind == DragKind.None)
        {
            return;
        }

        if (_dragKind == DragKind.MeasurementCreate)
        {
            var end = CoordinateAt(e.GetPosition(this)) ?? _previewPoint;
            if (_measureStart is { } start &&
                end is { } finish &&
                start.DistanceTo(finish) > 0.01 &&
                (_dragMoved || _completeMeasurementOnUp))
            {
                CompleteMeasurement(start, finish);
            }

            EndDrag();
            e.Handled = true;
            return;
        }

        if ((_dragKind is DragKind.MeasurementStart or DragKind.MeasurementEnd) &&
            _dragMoved &&
            _dragMeasurementNumber is { } number &&
            _dragMeasurementPoint is { } point)
        {
            MeasurementPointChanged?.Invoke(
                this,
                new MeasurementPointChangedEventArgs(
                    number,
                    _dragMeasurementIsStart,
                    point));
        }

        if ((_dragKind is DragKind.RulerLeft or DragKind.RulerRight or DragKind.RulerMove) &&
            _dragMoved)
        {
            CalibrationDraftChanged?.Invoke(this, EventArgs.Empty);
        }

        EndDrag();
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragKind == DragKind.None && Mode == MapInteractionMode.Measure && _measureStart is not null)
        {
            _previewPoint = null;
            InvalidateVisual();
        }
    }

    private void HandleCalibrationMouseDown(MapPoint coordinate, Point overlayPoint)
    {
        if (_draftLeft is null || _draftRight is null ||
            !TryGetOverlayPoint(_draftLeft.Value, out var left) ||
            !TryGetOverlayPoint(_draftRight.Value, out var right))
        {
            return;
        }

        if (Distance(left, overlayPoint) <= EndpointHitRadius)
        {
            StartDrag(DragKind.RulerLeft);
            return;
        }

        if (Distance(right, overlayPoint) <= EndpointHitRadius)
        {
            StartDrag(DragKind.RulerRight);
            return;
        }

        if (DistanceToSegment(overlayPoint, left, right) <= LineHitRadius)
        {
            StartDrag(DragKind.RulerMove);
            return;
        }

        if (_rulerCreateStart is { } createStart)
        {
            SetHorizontalDraft(createStart, coordinate);
            _rulerCreateStart = null;
            CalibrationDraftChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return;
        }

        _rulerCreateStart = coordinate;
        _draftLeft = coordinate;
        _draftRight = coordinate;
        StartDrag(DragKind.RulerCreate);
        CalibrationDraftChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void HandleMeasurementMouseDown(MapPoint coordinate)
    {
        _completeMeasurementOnUp = _measureStart is not null;
        _measureStart ??= coordinate;
        _previewPoint = coordinate;
        StartDrag(DragKind.MeasurementCreate);
        InvalidateVisual();
    }

    private void UpdateMeasurementDraft(Point overlayPoint)
    {
        var coordinate = CoordinateAt(overlayPoint);
        if (coordinate is null)
        {
            return;
        }

        _previewPoint = coordinate;
        if (Distance(overlayPoint, _dragOriginPoint) >= 4)
        {
            _dragMoved = true;
        }

        InvalidateVisual();
    }

    private void CompleteMeasurement(MapPoint start, MapPoint end)
    {
        _measureStart = null;
        _previewPoint = null;
        InvalidateVisual();
        MeasurementCompleted?.Invoke(this, new MeasurementCompletedEventArgs(start, end));
    }

    private bool HandleViewMouseDown(MapPoint coordinate, Point overlayPoint)
    {
        foreach (var record in _measurements.AsEnumerable().Reverse())
        {
            if (TryGetOverlayPoint(record.Start, out var start) &&
                Distance(start, overlayPoint) <= EndpointHitRadius)
            {
                SelectMeasurement(record.Number);
                BeginMeasurementDrag(record, isStart: true);
                return true;
            }

            if (TryGetOverlayPoint(record.End, out var end) &&
                Distance(end, overlayPoint) <= EndpointHitRadius)
            {
                SelectMeasurement(record.Number);
                BeginMeasurementDrag(record, isStart: false);
                return true;
            }
        }

        var hit = _measurements
            .AsEnumerable()
            .Reverse()
            .FirstOrDefault(record =>
                TryGetOverlayPoint(record.Start, out var start) &&
                TryGetOverlayPoint(record.End, out var end) &&
                DistanceToSegment(overlayPoint, start, end) <= LineHitRadius);
        if (hit is not null)
        {
            SelectMeasurement(hit.Number);
            return true;
        }

        return false;
    }

    private void BeginMeasurementDrag(MeasurementRecord record, bool isStart)
    {
        _dragMeasurementNumber = record.Number;
        _dragMeasurementIsStart = isStart;
        _dragMeasurementPoint = isStart ? record.Start : record.End;
        StartDrag(isStart ? DragKind.MeasurementStart : DragKind.MeasurementEnd);
    }

    private void UpdateMeasurementDrag(Point overlayPoint)
    {
        var coordinate = CoordinateAt(overlayPoint);
        if (coordinate is null || _dragMeasurementPoint is null)
        {
            return;
        }

        if (_dragMeasurementPoint.Value.DistanceTo(coordinate.Value) < 0.001)
        {
            return;
        }

        _dragMeasurementPoint = coordinate;
        _dragMoved = true;
        InvalidateVisual();
    }

    private void UpdateRulerDrag(Point overlayPoint)
    {
        var coordinate = CoordinateAt(overlayPoint);
        if (coordinate is null || _draftLeft is null || _draftRight is null)
        {
            return;
        }

        if (_dragKind == DragKind.RulerCreate)
        {
            if (_rulerCreateStart is { } createStart)
            {
                SetHorizontalDraft(createStart, coordinate.Value);
                if (Distance(overlayPoint, _dragOriginPoint) >= 4)
                {
                    _dragMoved = true;
                }
            }
        }
        else if (_dragKind == DragKind.RulerLeft)
        {
            _draftLeft = new MapPoint(coordinate.Value.X, _draftLeft.Value.Y);
        }
        else if (_dragKind == DragKind.RulerRight)
        {
            _draftRight = new MapPoint(coordinate.Value.X, _draftRight.Value.Y);
        }
        else if (TryGetCoordinateMap(out var map))
        {
            var previousScreen = map.OverlayDipToScreen(
                new ScreenPoint(_lastMousePosition.X, _lastMousePosition.Y));
            var currentScreen = map.OverlayDipToScreen(
                new ScreenPoint(overlayPoint.X, overlayPoint.Y));
            var deltaX = currentScreen.X - previousScreen.X;
            var deltaY = currentScreen.Y - previousScreen.Y;
            var left = _draftLeft.Value;
            var right = _draftRight.Value;
            var minX = Math.Min(left.X, right.X);
            var maxX = Math.Max(left.X, right.X);
            var minY = Math.Min(left.Y, right.Y);
            var maxY = Math.Max(left.Y, right.Y);
            deltaX = Math.Clamp(deltaX, -minX, map.CoordinateSpaceBounds.Width - maxX);
            deltaY = Math.Clamp(deltaY, -minY, map.CoordinateSpaceBounds.Height - maxY);
            _draftLeft = new MapPoint(left.X + deltaX, left.Y + deltaY);
            _draftRight = new MapPoint(right.X + deltaX, right.Y + deltaY);
        }

        _lastMousePosition = overlayPoint;
        _dragMoved = true;
        CalibrationDraftChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void DrawRuler(DrawingContext dc)
    {
        if (Mode != MapInteractionMode.Calibrate)
        {
            return;
        }

        var left = _draftLeft;
        var right = _draftRight;
        if (left is null || right is null ||
            !TryGetOverlayPoint(left.Value, out var a) ||
            !TryGetOverlayPoint(right.Value, out var b))
        {
            return;
        }

        var length = Math.Abs(right.Value.X - left.Value.X);
        if (length < 0.5)
        {
            if (_rulerCreateStart is not null)
            {
                DrawCalibrationEndpoint(dc, a);
                DrawLabel(
                    dc,
                    "点击第二点",
                    new Point(a.X, a.Y - 28),
                    DraftLabelBrush,
                    DraftTextBrush);
            }

            return;
        }

        dc.DrawLine(RulerShadowPen, a, b);
        dc.DrawLine(RulerPen, a, b);
        DrawCalibrationEndpoint(dc, a);
        DrawCalibrationEndpoint(dc, b);

        var midpoint = Midpoint(a, b);
        var normal = PerpendicularNormalized(a, b);
        var labelCenter = new Point(
            midpoint.X + (normal.X * 26),
            midpoint.Y + (normal.Y * 26));
        var label = $"600 米 · 水平 {length:0.#} px";
        DrawLabel(
            dc,
            label,
            labelCenter,
            DraftLabelBrush,
            DraftTextBrush);
    }

    private void DrawMeasurements(DrawingContext dc)
    {
        if (_calibration is null)
        {
            return;
        }

        foreach (var record in _measurements)
        {
            var start = record.Start;
            var end = record.End;
            if (_dragMeasurementNumber == record.Number)
            {
                if (_dragMeasurementIsStart && _dragMeasurementPoint is { } draggedStart)
                {
                    start = draggedStart;
                }
                else if (!_dragMeasurementIsStart && _dragMeasurementPoint is { } draggedEnd)
                {
                    end = draggedEnd;
                }
            }

            if (!TryGetOverlayPoint(start, out var a) ||
                !TryGetOverlayPoint(end, out var b))
            {
                continue;
            }

            var selected = SelectedMeasurementNumber == record.Number;
            dc.DrawLine(selected ? MeasurementSelectedShadowPen : MeasurementShadowPen, a, b);
            dc.DrawLine(selected ? MeasurementSelectedPen : MeasurementPen, a, b);
            DrawEndpoint(dc, a, selected ? AccentBrush : CyanBrush, selected ? 5 : 4.25);
            DrawEndpoint(dc, b, selected ? AccentBrush : CyanBrush, selected ? 5 : 4.25);

            var result = _calibration.Measure(start, end);
            var normal = PerpendicularNormalized(a, b);
            var midpoint = Midpoint(a, b);
            var offset = 20d + ((record.Number % 3) * 14d);
            var labelCenter = new Point(
                midpoint.X + (normal.X * offset),
                midpoint.Y + (normal.Y * offset));
            DrawLabel(
                dc,
                $"#{record.Number}  {result.StraightMeters:0} 米",
                labelCenter,
                selected ? SelectedLabelBrush : MeasurementLabelBrush,
                selected ? SelectedTextBrush : BrightTextBrush);
        }
    }

    private void DrawMeasurementPreview(DrawingContext dc)
    {
        if (Mode != MapInteractionMode.Measure ||
            _measureStart is null ||
            _previewPoint is null ||
            _calibration is null ||
            !TryGetOverlayPoint(_measureStart.Value, out var a) ||
            !TryGetOverlayPoint(_previewPoint.Value, out var b))
        {
            return;
        }

        dc.DrawLine(PreviewShadowPen, a, b);
        dc.DrawLine(PreviewPen, a, b);
        DrawEndpoint(dc, a, CyanBrush, 5);
        var result = _calibration.Measure(_measureStart.Value, _previewPoint.Value);
        var midpoint = Midpoint(a, b);
        DrawLabel(
            dc,
            $"预览 {result.StraightMeters:0} 米",
            new Point(midpoint.X, midpoint.Y - 22),
            PreviewLabelBrush,
            BrightTextBrush);
    }

    private void DrawEndpoint(DrawingContext dc, Point center, Brush fill, double radius = 7)
    {
        dc.DrawEllipse(fill, EndpointOutlinePen, center, radius, radius);
    }

    private void DrawCalibrationEndpoint(DrawingContext dc, Point center)
    {
        const double horizontalHalfLength = 8;
        const double verticalHalfLength = 13;
        var horizontalStart = new Point(center.X - horizontalHalfLength, center.Y);
        var horizontalEnd = new Point(center.X + horizontalHalfLength, center.Y);
        var verticalStart = new Point(center.X, center.Y - verticalHalfLength);
        var verticalEnd = new Point(center.X, center.Y + verticalHalfLength);

        dc.DrawLine(CalibrationEndpointShadowPen, horizontalStart, horizontalEnd);
        dc.DrawLine(CalibrationEndpointShadowPen, verticalStart, verticalEnd);
        dc.DrawLine(CalibrationEndpointPen, horizontalStart, horizontalEnd);
        dc.DrawLine(CalibrationEndpointPen, verticalStart, verticalEnd);
    }

    private void DrawLabel(
        DrawingContext dc,
        string text,
        Point center,
        Brush background,
        Brush foreground)
    {
        var formatted = CreateText(text, 13, foreground, FontWeights.SemiBold);
        var width = formatted.Width + 18;
        var height = formatted.Height + 12;
        var rect = new Rect(center.X - (width / 2), center.Y - (height / 2), width, height);
        rect.X = Math.Clamp(rect.X, 4, Math.Max(4, ActualWidth - rect.Width - 4));
        rect.Y = Math.Clamp(rect.Y, 4, Math.Max(4, ActualHeight - rect.Height - 4));
        dc.DrawRoundedRectangle(background, LabelOutlinePen, rect, 5, 5);
        dc.DrawText(
            formatted,
            new Point(rect.X + 9, rect.Y + ((rect.Height - formatted.Height) / 2)));
    }

    private void CreateDefaultRuler()
    {
        if (_coordinateSpace is null)
        {
            return;
        }

        var width = _coordinateSpace.Bounds.Width;
        var height = _coordinateSpace.Bounds.Height;
        var length = Math.Clamp(Math.Min(width, height) * 0.42, 80, Math.Min(width, 640));
        var centerX = width / 2;
        var centerY = height / 2;
        _draftLeft = new MapPoint(centerX - (length / 2), centerY);
        _draftRight = new MapPoint(centerX + (length / 2), centerY);
    }

    private void SetHorizontalDraft(MapPoint first, MapPoint second)
    {
        var y = (first.Y + second.Y) / 2;
        var leftX = Math.Min(first.X, second.X);
        var rightX = Math.Max(first.X, second.X);
        if (_coordinateSpace is not null)
        {
            leftX = Math.Clamp(leftX, 0, _coordinateSpace.Bounds.Width);
            rightX = Math.Clamp(rightX, 0, _coordinateSpace.Bounds.Width);
            y = Math.Clamp(y, 0, _coordinateSpace.Bounds.Height);
        }

        _draftLeft = new MapPoint(leftX, y);
        _draftRight = new MapPoint(rightX, y);
    }

    private void StartDrag(DragKind kind)
    {
        _dragKind = kind;
        _dragMoved = false;
        _dragOriginPoint = Mouse.GetPosition(this);
        CaptureMouse();
    }

    private void EndDrag()
    {
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        _dragKind = DragKind.None;
        _dragMeasurementNumber = null;
        _dragMeasurementPoint = null;
        _dragMoved = false;
        _completeMeasurementOnUp = false;
        Cursor = Mode switch
        {
            MapInteractionMode.Calibrate => Cursors.Hand,
            MapInteractionMode.Measure => Cursors.Cross,
            _ => Cursors.Arrow
        };
    }

    private void UpdateHoverCursor(Point overlayPoint)
    {
        if (_dragKind != DragKind.None)
        {
            return;
        }

        if (Mode == MapInteractionMode.Calibrate &&
            _draftLeft is not null &&
            _draftRight is not null &&
            ((TryGetOverlayPoint(_draftLeft.Value, out var left) &&
              Distance(left, overlayPoint) <= EndpointHitRadius) ||
             (TryGetOverlayPoint(_draftRight.Value, out var right) &&
              Distance(right, overlayPoint) <= EndpointHitRadius)))
        {
            Cursor = Cursors.SizeAll;
            return;
        }

        if (Mode == MapInteractionMode.View)
        {
            var overEndpoint = _measurements.Any(record =>
                (TryGetOverlayPoint(record.Start, out var start) &&
                 Distance(start, overlayPoint) <= EndpointHitRadius) ||
                (TryGetOverlayPoint(record.End, out var end) &&
                 Distance(end, overlayPoint) <= EndpointHitRadius));
            if (overEndpoint)
            {
                Cursor = Cursors.SizeAll;
                return;
            }
        }

        Cursor = Mode switch
        {
            MapInteractionMode.Calibrate => Cursors.Hand,
            MapInteractionMode.Measure => Cursors.Cross,
            _ => Cursors.Arrow
        };
    }

    private void SelectMeasurement(int number)
    {
        SelectedMeasurementNumber = number;
        InvalidateVisual();
        MeasurementSelected?.Invoke(this, number);
    }

    private MapPoint? CoordinateAt(Point overlayPoint)
    {
        if (!TryGetCoordinateMap(out var map))
        {
            return null;
        }

        var coordinate = map.OverlayDipToCoordinate(
            new ScreenPoint(overlayPoint.X, overlayPoint.Y));
        return map.ContainsCoordinate(coordinate) ? coordinate : null;
    }

    private bool TryGetOverlayPoint(MapPoint coordinate, out Point point)
    {
        if (!TryGetCoordinateMap(out var map) || !map.ContainsCoordinate(coordinate))
        {
            point = default;
            return false;
        }

        var overlay = map.CoordinateToOverlayDip(coordinate);
        point = new Point(overlay.X, overlay.Y);
        return true;
    }

    private bool TryGetCoordinateMap(out UnifiedCoordinateMap map)
    {
        map = null!;
        var window = Window.GetWindow(this);
        if (window is null || _coordinateSpace is not { IsValid: true })
        {
            return false;
        }

        var overlayBounds = OverlayWindowController.GetClientBounds(window);
        if (!overlayBounds.IsValid)
        {
            return false;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        map = new UnifiedCoordinateMap(
            _coordinateSpace.Bounds,
            overlayBounds,
            dpi.DpiScaleX,
            dpi.DpiScaleY);
        return true;
    }

    private static Point Midpoint(Point a, Point b) =>
        new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double DistanceToSegment(Point point, Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= double.Epsilon)
        {
            return Distance(point, start);
        }

        var t = (((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        return Distance(point, new Point(start.X + (t * dx), start.Y + (t * dy)));
    }

    private static Point PerpendicularNormalized(Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= double.Epsilon)
        {
            return new Point(0, -1);
        }

        var nx = -dy / length;
        var ny = dx / length;
        return ny > 0 ? new Point(-nx, -ny) : new Point(nx, ny);
    }

    private FormattedText CreateText(string text, double size, Brush brush, FontWeight weight) =>
        new(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(UiFontFamily, FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness, DashStyle? dashStyle = null)
    {
        var pen = new Pen(brush, thickness);
        if (dashStyle is not null)
        {
            pen.DashStyle = dashStyle;
        }

        pen.Freeze();
        return pen;
    }

    private static readonly FontFamily UiFontFamily = new("Microsoft YaHei UI");
    private static readonly Brush AccentBrush = FrozenBrush(Color.FromRgb(255, 174, 64));
    private static readonly Brush CyanBrush = FrozenBrush(Color.FromRgb(62, 226, 255));
    private static readonly Brush BrightTextBrush = FrozenBrush(Color.FromRgb(248, 251, 255));
    private static readonly Brush DraftTextBrush = FrozenBrush(Color.FromRgb(255, 219, 157));
    private static readonly Brush SelectedTextBrush = FrozenBrush(Color.FromRgb(255, 223, 166));
    private static readonly Brush DraftLabelBrush = FrozenBrush(Color.FromArgb(236, 76, 45, 1));
    private static readonly Brush MeasurementLabelBrush = FrozenBrush(Color.FromArgb(228, 5, 34, 45));
    private static readonly Brush SelectedLabelBrush = FrozenBrush(Color.FromArgb(238, 68, 43, 2));
    private static readonly Brush PreviewLabelBrush = FrozenBrush(Color.FromArgb(228, 5, 43, 54));
    private static readonly Pen EndpointOutlinePen = FrozenPen(
        FrozenBrush(Color.FromArgb(255, 7, 13, 18)),
        1.6);
    private static readonly Pen CalibrationEndpointShadowPen = FrozenPen(
        FrozenBrush(Color.FromArgb(210, 0, 0, 0)),
        2.8);
    private static readonly Pen CalibrationEndpointPen = FrozenPen(AccentBrush, 1.15);
    private static readonly Pen RulerPen = FrozenPen(AccentBrush, 1.2);
    private static readonly Pen RulerShadowPen = FrozenPen(
        FrozenBrush(Color.FromArgb(210, 0, 0, 0)),
        3.2);
    private static readonly Pen MeasurementPen = FrozenPen(CyanBrush, 1.8);
    private static readonly Pen MeasurementShadowPen = FrozenPen(
        FrozenBrush(Color.FromArgb(210, 0, 0, 0)),
        4.4);
    private static readonly Pen MeasurementSelectedPen = FrozenPen(AccentBrush, 2.5);
    private static readonly Pen MeasurementSelectedShadowPen = FrozenPen(
        FrozenBrush(Color.FromArgb(220, 0, 0, 0)),
        5.8);
    private static readonly Pen PreviewPen = FrozenPen(
        FrozenBrush(Color.FromArgb(245, 107, 236, 255)),
        1.5,
        new DashStyle([5, 4], 0));
    private static readonly Pen PreviewShadowPen = FrozenPen(
        FrozenBrush(Color.FromArgb(190, 0, 0, 0)),
        3.8);
    private static readonly Pen LabelOutlinePen = FrozenPen(
        FrozenBrush(Color.FromArgb(175, 255, 255, 255)),
        1);

    private enum DragKind
    {
        None,
        RulerLeft,
        RulerRight,
        RulerMove,
        RulerCreate,
        MeasurementStart,
        MeasurementEnd,
        MeasurementCreate
    }
}
