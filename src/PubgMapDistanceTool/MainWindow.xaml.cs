using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PubgMapDistanceTool.Controls;
using PubgMapDistanceTool.Core.CalibrationModel;
using PubgMapDistanceTool.Core.Coordinates;
using PubgMapDistanceTool.Core.Measurements;
using PubgMapDistanceTool.Platform;

namespace PubgMapDistanceTool;

public partial class MainWindow : Window
{
    private const int WmDisplayChange = 0x007E;
    private const int WmDpiChanged = 0x02E0;
    private static readonly Brush InteractiveOverlayBackground =
        new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
    private readonly MeasurementManager _manager = new();
    private readonly GameWindowTracker _gameTracker = new();
    private readonly HotkeyService _hotkeys = new();
    private readonly DispatcherTimer _gameTimer;
    private readonly DispatcherTimer _toastTimer;
    private HotkeySettings _hotkeySettings;
    private HwndSource? _windowSource;
    private CoordinateSpace? _activeSpace;
    private GameWindowSnapshot? _lastGame;
    private int? _selectedNumber;
    private bool _clickThrough;
    private bool _panelCollapsed;
    private bool _normalMode;
    private bool _followGameWindow = true;
    private bool _gameUnavailableAnnounced;
    private bool _resizing;
    private bool _settingsOpen;
    private bool _suppressAutoHide;
    private Point _resizeStartScreen;
    private double _resizeStartWidth;
    private double _resizeStartHeight;

    public MainWindow()
    {
        InitializeComponent();
        var arguments = Environment.GetCommandLineArgs();
        _normalMode = arguments.Any(
            argument => string.Equals(argument, "--normal", StringComparison.OrdinalIgnoreCase));
        if (_normalMode)
        {
            Topmost = false;
            ShowInTaskbar = true;
            RootGrid.Background = new SolidColorBrush(Color.FromArgb(255, 9, 14, 20));
        }

        _hotkeySettings = HotkeySettingsStore.Load();
        _gameTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _gameTimer.Tick += GameTimer_Tick;
        _toastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3.5)
        };
        _toastTimer.Tick += ToastTimer_Tick;

        OverlayCanvas.MeasurementCompleted += OverlayCanvas_MeasurementCompleted;
        OverlayCanvas.MeasurementPointChanged += OverlayCanvas_MeasurementPointChanged;
        OverlayCanvas.MeasurementSelected += OverlayCanvas_MeasurementSelected;
        OverlayCanvas.CalibrationDraftChanged += OverlayCanvas_CalibrationDraftChanged;
        UpdateUi();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = OverlayWindowController.GetHandle(this);
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowProc);
        _hotkeys.ActionRequested += Hotkeys_ActionRequested;
        var errors = _hotkeys.Register(_hotkeySettings);
        if (errors.Count > 0)
        {
            _ = Dispatcher.BeginInvoke(() =>
                SetStatus(string.Join("；", errors), isWarning: true, showToast: true));
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshCoordinateSpace(force: true, announce: false);
        _gameTimer.Start();

        if (!_hotkeys.IsRegistered(HotkeyAction.ToggleWindow))
        {
            HideButton.IsEnabled = false;
            HideButton.ToolTip = "显示/隐藏快捷键监听失败，请先在快捷键设置中更换组合键。";
        }

        if (_activeSpace?.Kind == CoordinateSpaceKind.DesktopFallback)
        {
            ShowToast("未检测到 PUBG；当前使用桌面坐标，可测试绘制，但正式测量请先启动窗口化或无边框游戏。", 6000);
        }

        UpdateUi();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _gameTimer.Stop();
        _toastTimer.Stop();
        _hotkeys.UnregisterAll();
        _windowSource?.RemoveHook(WindowProc);
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!IsVisible ||
            WindowState == WindowState.Minimized ||
            _settingsOpen ||
            _suppressAutoHide ||
            _normalMode)
        {
            return;
        }

        // Keep the overlay available when Alt+Tab moves focus back to a
        // fullscreen-windowed game. Reassert topmost without activating so
        // switching applications does not hide the tool or steal input.
        _ = Dispatcher.BeginInvoke(
            () => OverlayWindowController.ReassertTopmost(this),
            DispatcherPriority.Background);
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
        {
            if (!_normalMode)
            {
                Topmost = true;
            }

            _ = Dispatcher.BeginInvoke(OverlayCanvas.RefreshDrawing, DispatcherPriority.Render);
        }
    }

    private IntPtr WindowProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (_hotkeys.HandleMessage(message, wParam, lParam))
        {
            handled = true;
            return IntPtr.Zero;
        }

        if (message is WmDisplayChange or WmDpiChanged)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                RefreshCoordinateSpace(force: true, announce: false);
                OverlayCanvas.RefreshDrawing();
            });
        }

        return IntPtr.Zero;
    }

    private void Hotkeys_ActionRequested(object? sender, HotkeyAction action)
    {
        if (_settingsOpen)
        {
            return;
        }

        switch (action)
        {
            case HotkeyAction.ToggleWindow:
                ToggleWindowVisibility();
                break;
            case HotkeyAction.Calibration:
                ShowOverlayForEditing();
                BeginCalibration();
                break;
            case HotkeyAction.Measurement:
                ShowOverlayForEditing();
                BeginMeasurement();
                break;
            case HotkeyAction.ToggleClickThrough:
                ToggleClickThrough();
                break;
        }
    }

    private void GameTimer_Tick(object? sender, EventArgs e)
    {
        RefreshCoordinateSpace(force: false, announce: true);
        UpdateUi();
    }

    private void ToastTimer_Tick(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        TransientToast.Visibility = Visibility.Collapsed;
    }

    private void RefreshCoordinateSpace(bool force, bool announce)
    {
        if (_gameTracker.TryFind(out var game))
        {
            _lastGame = game;
            _gameUnavailableAnnounced = false;
            var gameSpace = GameWindowTracker.CreateGameSpace(game);
            if (_activeSpace is null || !string.Equals(_activeSpace.Id, gameSpace.Id, StringComparison.Ordinal))
            {
                var previous = _activeSpace;
                ApplyCoordinateSpace(gameSpace);
                if (previous is not null)
                {
                    SetStatus(
                        "检测到游戏窗口或显示尺寸发生变化，旧校准和旧测量已清除，请重新校准。",
                        isWarning: true,
                        showToast: true);
                }
                else if (announce)
                {
                    SetStatus("已绑定 PUBG 客户区，可以开始 600 米校准。");
                }
            }
            else
            {
                var boundsChanged =
                    Math.Abs(_activeSpace.Bounds.X - gameSpace.Bounds.X) > 0.5 ||
                    Math.Abs(_activeSpace.Bounds.Y - gameSpace.Bounds.Y) > 0.5;
                if (boundsChanged || force)
                {
                    _activeSpace = gameSpace;
                    OverlayCanvas.SetCoordinateSpace(_activeSpace);
                    OverlayCanvas.RefreshDrawing();
                }

                OverlayCanvas.CanInteract = true;
                if (_followGameWindow)
                {
                    SnapOverlayIfNeeded();
                }
            }
        }
        else if (_activeSpace?.Kind == CoordinateSpaceKind.GameClient)
        {
            OverlayCanvas.CanInteract = false;
            if (!_gameUnavailableAnnounced)
            {
                _gameUnavailableAnnounced = true;
                SetStatus(
                    "PUBG 窗口暂不可见或被最小化，测量输入已暂停；返回同一游戏窗口后可继续。",
                    isWarning: true,
                    showToast: true);
            }
        }
        else if (_activeSpace is null)
        {
            ApplyCoordinateSpace(GameWindowTracker.CreateDesktopSpace());
        }

        UpdateUi();
    }

    private void ApplyCoordinateSpace(CoordinateSpace space)
    {
        var hadSession =
            _manager.Calibration is not null ||
            _manager.Records.Count > 0;
        _activeSpace = space;
        _selectedNumber = null;
        _manager.SetCoordinateSpace(space.Id);
        OverlayCanvas.SetCoordinateSpace(space);
        OverlayCanvas.SetCalibration(null);
        OverlayCanvas.SetMeasurements([]);
        OverlayCanvas.SelectedMeasurementNumber = null;
        OverlayCanvas.CanInteract = true;
        if (_followGameWindow)
        {
            SnapOverlayToActiveSpace();
        }

        UpdateUi();
        SetStatus(
            space.Kind == CoordinateSpaceKind.GameClient
                ? "已绑定 PUBG 客户区，可以开始 600 米校准。"
                : "未检测到 PUBG；当前使用桌面坐标，可测试绘制，正式测量请先启动窗口化/无边框游戏。",
            isWarning: space.Kind == CoordinateSpaceKind.DesktopFallback);
        if (hadSession && space.Kind == CoordinateSpaceKind.GameClient)
        {
            ShowToast("游戏坐标空间已改变，为避免错误结果，旧校准与测量记录已清除。", 4500);
        }
    }

    private void SnapOverlayIfNeeded()
    {
        if (_activeSpace is null)
        {
            return;
        }

        var current = OverlayWindowController.GetClientBounds(this);
        var target = _activeSpace.Bounds;
        if (Math.Abs(current.X - target.X) <= 1 &&
            Math.Abs(current.Y - target.Y) <= 1 &&
            Math.Abs(current.Width - target.Width) <= 1 &&
            Math.Abs(current.Height - target.Height) <= 1)
        {
            return;
        }

        OverlayWindowController.SnapToBounds(this, target, topmost: !_normalMode);
        _ = Dispatcher.BeginInvoke(OverlayCanvas.RefreshDrawing, DispatcherPriority.Render);
    }

    private void SnapOverlayToActiveSpace()
    {
        if (_activeSpace is null)
        {
            return;
        }

        OverlayWindowController.SnapToBounds(this, _activeSpace.Bounds, topmost: !_normalMode);
        _ = Dispatcher.BeginInvoke(OverlayCanvas.RefreshDrawing, DispatcherPriority.Render);
    }

    private void CalibrateButton_Click(object sender, RoutedEventArgs e) => BeginCalibration();

    private void RecalibrateButton_Click(object sender, RoutedEventArgs e) => BeginCalibration();

    private void CompleteCalibrationButton_Click(object sender, RoutedEventArgs e) => CompleteCalibration();

    private void MeasureButton_Click(object sender, RoutedEventArgs e)
    {
        if (OverlayCanvas.Mode == MapInteractionMode.Measure)
        {
            OverlayCanvas.CancelCurrentMode();
            _panelCollapsed = false;
            ApplyPanelState();
            SetStatus("已结束测距模式。");
            UpdateUi();
            return;
        }

        BeginMeasurement();
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e) => UndoLastAction();

    private void DeleteButton_Click(object sender, RoutedEventArgs e) => DeleteSelectedMeasurement();

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_manager.Records.Count == 0)
        {
            return;
        }

        MessageBoxResult result;
        _suppressAutoHide = true;
        try
        {
            result = MessageBox.Show(
                this,
                "清除全部测量记录？当前 600 米校准比例会保留。",
                "清除测量",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
        }
        finally
        {
            _suppressAutoHide = false;
        }

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _manager.ClearMeasurements();
        _selectedNumber = null;
        ApplySessionToCanvas();
        SetStatus("已清除全部测量记录。", showToast: true);
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNumber is not { } number ||
            _manager.Find(number) is not { } record ||
            _manager.Calibration is not { } calibration)
        {
            return;
        }

        var result = record.Calculate(calibration);
        var text =
            $"测量编号：{record.Number}{Environment.NewLine}" +
            $"直线距离：{result.StraightMeters:0.0} 米{Environment.NewLine}" +
            $"水平距离：{result.HorizontalMeters:0.0} 米{Environment.NewLine}" +
            $"垂直距离：{result.VerticalMeters:0.0} 米{Environment.NewLine}" +
            $"起点客户区像素：({record.Start.X:0.###}, {record.Start.Y:0.###}){Environment.NewLine}" +
            $"终点客户区像素：({record.End.X:0.###}, {record.End.Y:0.###})";
        try
        {
            Clipboard.SetText(text);
            SetStatus($"测量 #{record.Number} 已复制。", showToast: true);
        }
        catch (Exception ex)
        {
            SetStatus($"复制失败：{ex.Message}", isWarning: true, showToast: true);
        }
    }

    private void ClickThroughButton_Click(object sender, RoutedEventArgs e) => ToggleClickThrough();

    private void SnapButton_Click(object sender, RoutedEventArgs e)
    {
        _followGameWindow = true;
        SnapOverlayToActiveSpace();
        SetStatus(
            _activeSpace?.Kind == CoordinateSpaceKind.GameClient
                ? "悬浮窗已贴合 PUBG 客户区。"
                : "未检测到 PUBG，已贴合当前桌面范围。",
            showToast: true);
    }

    private void NormalModeButton_Click(object sender, RoutedEventArgs e) => ToggleNormalMode();

    private void HotkeySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowOverlayForEditing();
        _settingsOpen = true;
        try
        {
            var dialog = new HotkeySettingsWindow(_hotkeySettings)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            _hotkeySettings = dialog.Result;
            HotkeySettingsStore.Save(_hotkeySettings);
            var errors = _hotkeys.Register(_hotkeySettings);
            SetStatus(
                errors.Count == 0
                    ? "快捷键已保存并立即生效。"
                    : $"快捷键已保存，但部分注册失败：{string.Join("；", errors)}",
                isWarning: errors.Count > 0,
                showToast: true);
        }
        catch (Exception ex)
        {
            SetStatus($"无法保存快捷键：{ex.Message}", isWarning: true, showToast: true);
        }
        finally
        {
            _settingsOpen = false;
            UpdateUi();
        }
    }

    private void HideButton_Click(object sender, RoutedEventArgs e) => ToggleWindowVisibility();

    private void ExitButton_Click(object sender, RoutedEventArgs e) => Close();

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        _panelCollapsed = true;
        ApplyPanelState();
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        _panelCollapsed = false;
        ApplyPanelState();
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _followGameWindow = false;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        OverlayCanvas.RefreshDrawing();
        SetStatus("悬浮窗已自由移动；测距坐标仍锚定到游戏客户区。");
        e.Handled = true;
    }

    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _resizing = true;
        _followGameWindow = false;
        _resizeStartScreen = PointToScreen(e.GetPosition(this));
        _resizeStartWidth = ActualWidth;
        _resizeStartHeight = ActualHeight;
        ResizeGrip.CaptureMouse();
        e.Handled = true;
    }

    private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizing)
        {
            return;
        }

        var current = PointToScreen(e.GetPosition(this));
        var dpi = VisualTreeHelper.GetDpi(this);
        Width = Math.Max(MinWidth, _resizeStartWidth + ((current.X - _resizeStartScreen.X) / dpi.DpiScaleX));
        Height = Math.Max(MinHeight, _resizeStartHeight + ((current.Y - _resizeStartScreen.Y) / dpi.DpiScaleY));
        OverlayCanvas.RefreshDrawing();
        e.Handled = true;
    }

    private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing)
        {
            return;
        }

        _resizing = false;
        ResizeGrip.ReleaseMouseCapture();
        SetStatus("悬浮窗大小已调整；游戏坐标数据未改变。");
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (OverlayCanvas.Mode == MapInteractionMode.Measure)
            {
                OverlayCanvas.CancelPendingMeasurement();
                SetStatus("已取消当前未完成的测距起点。");
            }
            else if (OverlayCanvas.Mode == MapInteractionMode.Calibrate)
            {
                OverlayCanvas.CancelCurrentMode();
                OverlayCanvas.SetCalibration(_manager.Calibration);
                SetStatus("已取消校准草稿，原校准比例保持不变。");
            }

            UpdateUi();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            UndoLastAction();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete)
        {
            DeleteSelectedMeasurement();
            e.Handled = true;
        }
    }

    private void BeginCalibration()
    {
        if (_activeSpace is null)
        {
            SetStatus("坐标空间尚未就绪，请稍候再试。", isWarning: true, showToast: true);
            return;
        }

        if (_activeSpace.Kind == CoordinateSpaceKind.DesktopFallback)
        {
            SetStatus(
                "未检测到 PUBG。校准将使用桌面坐标，正式游戏测量前请启动窗口化/无边框游戏。",
                isWarning: true,
                showToast: true);
        }

        OverlayCanvas.EnterCalibration();
        SetStatus(
            _manager.Calibration is null
                ? "校准：直接点击两个网格点或拖取一条线，系统会自动校正为水平；十字端点可再精调。"
                : "重新校准：直接点击两点/拖取会自动校平；旧比例在确认前仍然生效。");
        UpdateUi();
    }

    private void CompleteCalibration()
    {
        if (OverlayCanvas.Mode != MapInteractionMode.Calibrate ||
            OverlayCanvas.DraftLeft is not { } left ||
            OverlayCanvas.DraftRight is not { } right)
        {
            return;
        }

        if (OverlayCanvas.DraftPixelLength < 2)
        {
            SetStatus("基准尺过短，请拉开两个端点后再完成校准。", isWarning: true, showToast: true);
            return;
        }

        if (_manager.CoordinateSpaceId is null)
        {
            SetStatus("游戏坐标空间尚未就绪，无法保存校准。", isWarning: true, showToast: true);
            return;
        }

        try
        {
            var calibration = new Calibration(left, right, _manager.CoordinateSpaceId);
            _manager.ConfirmCalibration(calibration);
            OverlayCanvas.SetCalibration(calibration);
            OverlayCanvas.CancelCurrentMode();
            ApplySessionToCanvas();
            BeginMeasurement();
            SetStatus(
                $"校准完成：600 米 = {calibration.PixelLength:0.#} 像素，" +
                $"每像素 {calibration.MetersPerPixel:0.#####} 米。已自动收起控制条，可全屏按住左键拉线。",
                showToast: true);
        }
        catch (Exception ex)
        {
            SetStatus($"校准失败：{ex.Message}", isWarning: true, showToast: true);
        }
    }

    private void BeginMeasurement()
    {
        if (!_manager.CanMeasure)
        {
            SetStatus("请先用 600 米基准尺完成校准。", isWarning: true, showToast: true);
            return;
        }

        if (!OverlayCanvas.CanInteract)
        {
            SetStatus("PUBG 窗口当前不可用，无法开始测距。", isWarning: true, showToast: true);
            return;
        }

        OverlayCanvas.EnterMeasurement();
        _panelCollapsed = true;
        ApplyPanelState();
        SetStatus("测距：在任意位置按住左键拖到终点后松开；也可点击起点、再点击终点。");
        UpdateUi();
    }

    private void UndoLastAction()
    {
        if (OverlayCanvas.Mode == MapInteractionMode.Calibrate)
        {
            SetStatus("请先完成或取消当前校准，再使用撤销。", isWarning: true, showToast: true);
            return;
        }

        if (!_manager.Undo())
        {
            SetStatus("没有可撤销的操作。");
            return;
        }

        if (_selectedNumber is { } number && _manager.Find(number) is null)
        {
            _selectedNumber = null;
        }

        if (!_manager.CanMeasure && OverlayCanvas.Mode == MapInteractionMode.Measure)
        {
            OverlayCanvas.CancelCurrentMode();
        }

        ApplySessionToCanvas();
        SetStatus("已撤销上一项操作。", showToast: true);
    }

    private void DeleteSelectedMeasurement()
    {
        if (_selectedNumber is not { } number || _manager.Find(number) is null)
        {
            SetStatus("请先在画布上点击要删除的测量线。", isWarning: true, showToast: true);
            return;
        }

        _manager.DeleteMeasurement(number);
        _selectedNumber = null;
        ApplySessionToCanvas();
        SetStatus($"已删除测量 #{number}。", showToast: true);
    }

    private void OverlayCanvas_MeasurementCompleted(object? sender, MeasurementCompletedEventArgs e)
    {
        try
        {
            var record = _manager.AddMeasurement(e.Start, e.End);
            _selectedNumber = record.Number;
            ApplySessionToCanvas();
            var result = record.Calculate(_manager.Calibration!);
            SetStatus($"测量 #{record.Number}：{result.StraightMeters:0.0} 米。继续点击可测下一组。");
        }
        catch (Exception ex)
        {
            SetStatus($"无法保存测量：{ex.Message}", isWarning: true, showToast: true);
        }
    }

    private void OverlayCanvas_MeasurementPointChanged(object? sender, MeasurementPointChangedEventArgs e)
    {
        if (_manager.Find(e.Number) is not { } record)
        {
            return;
        }

        var start = e.IsStart ? e.Point : record.Start;
        var end = e.IsStart ? record.End : e.Point;
        try
        {
            _manager.UpdateMeasurementPoints(e.Number, start, end);
            _selectedNumber = e.Number;
            ApplySessionToCanvas();
            SetStatus($"测量 #{e.Number} 的端点已更新，距离已重新计算。");
        }
        catch (Exception ex)
        {
            ApplySessionToCanvas();
            SetStatus($"端点更新失败：{ex.Message}", isWarning: true, showToast: true);
        }
    }

    private void OverlayCanvas_MeasurementSelected(object? sender, int number)
    {
        _selectedNumber = number;
        OverlayCanvas.SelectedMeasurementNumber = number;
        OverlayCanvas.RefreshDrawing();
        UpdateUi();
    }

    private void OverlayCanvas_CalibrationDraftChanged(object? sender, EventArgs e) => UpdateUi();

    private void ApplySessionToCanvas()
    {
        OverlayCanvas.SetCalibration(_manager.Calibration);
        OverlayCanvas.SetMeasurements(_manager.Records);
        OverlayCanvas.SelectedMeasurementNumber = _selectedNumber;
        OverlayCanvas.RefreshDrawing();
        UpdateUi();
    }

    private void ToggleWindowVisibility()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            Hide();
            return;
        }

        WindowState = WindowState.Normal;
        Show();
        Activate();
        if (!_normalMode)
        {
            Topmost = true;
        }

        OverlayCanvas.RefreshDrawing();
    }

    private void ToggleClickThrough()
    {
        if (!_clickThrough && !_hotkeys.IsRegistered(HotkeyAction.ToggleClickThrough))
        {
            SetStatus("鼠标穿透快捷键未注册，无法安全切换。请先在快捷键设置中更换组合键。", isWarning: true, showToast: true);
            return;
        }

        _clickThrough = !_clickThrough;
        if (_clickThrough)
        {
            OverlayCanvas.CancelPendingMeasurement();
            _panelCollapsed = true;
        }
        else
        {
            _panelCollapsed = false;
        }

        OverlayWindowController.SetClickThrough(this, _clickThrough);
        UpdateUi();
        SetStatus(
            _clickThrough
                ? $"鼠标穿透已开启，现在可正常操作游戏；按 {DisplayHotkey(_hotkeySettings.ToggleClickThrough)} 返回编辑。"
                : "鼠标穿透已关闭，回到测距编辑状态。",
            showToast: true);
    }

    private void ToggleNormalMode()
    {
        _normalMode = !_normalMode;
        Topmost = !_normalMode;
        ShowInTaskbar = true;
        RootGrid.Background = _normalMode
            ? new SolidColorBrush(Color.FromArgb(255, 9, 14, 20))
            : InteractiveOverlayBackground;
        ResizeGrip.Visibility = _normalMode || (!_panelCollapsed && !_clickThrough)
            ? Visibility.Visible
            : Visibility.Collapsed;
        SetStatus(
            _normalMode
                ? "普通模式：不透明窗口、显示任务栏并取消置顶，用作透明层不可用时的回退。"
                : "已返回透明置顶悬浮模式。",
            showToast: true);
        UpdateUi();
    }

    private void ShowOverlayForEditing()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        if (_clickThrough)
        {
            _clickThrough = false;
            OverlayWindowController.SetClickThrough(this, false);
        }

        _panelCollapsed = false;
        ApplyPanelState();
        if (!_normalMode)
        {
            Topmost = true;
        }
    }

    private void ApplyPanelState()
    {
        var showMini = _panelCollapsed || _clickThrough;
        ControlPanel.Visibility = showMini ? Visibility.Collapsed : Visibility.Visible;
        MiniPanel.Visibility = showMini ? Visibility.Visible : Visibility.Collapsed;
        ResizeGrip.Visibility = !_clickThrough && !_panelCollapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateUi()
    {
        var isCalibrating = OverlayCanvas.Mode == MapInteractionMode.Calibrate;
        var isMeasuring = OverlayCanvas.Mode == MapInteractionMode.Measure;
        var calibration = _manager.Calibration;
        var canMeasure = _manager.CanMeasure && OverlayCanvas.CanInteract;
        var hasSelection =
            _selectedNumber is { } selected &&
            _manager.Find(selected) is not null;

        CalibrateButton.IsEnabled = _activeSpace is not null && !isCalibrating && calibration is null;
        CompleteCalibrationButton.IsEnabled = isCalibrating;
        RecalibrateButton.IsEnabled = _activeSpace is not null && !isCalibrating && calibration is not null;
        MeasureButton.IsEnabled = canMeasure || isMeasuring;
        MeasureButton.Content = isMeasuring ? "结束测距" : "测距";
        UndoButton.IsEnabled = _manager.CanUndo && !isCalibrating;
        DeleteButton.IsEnabled = hasSelection && !isCalibrating;
        ClearButton.IsEnabled = _manager.Records.Count > 0 && !isCalibrating;
        CopyButton.IsEnabled = hasSelection && !isCalibrating;
        ClickThroughButton.IsEnabled =
            _clickThrough || _hotkeys.IsRegistered(HotkeyAction.ToggleClickThrough);
        SnapButton.IsEnabled = _activeSpace is not null;
        HideButton.IsEnabled = _hotkeys.IsRegistered(HotkeyAction.ToggleWindow);
        NormalModeButton.Content = _normalMode ? "悬浮模式" : "普通模式";
        var showKey = DisplayHotkey(_hotkeySettings.ToggleWindow);
        var calibrationKey = DisplayHotkey(_hotkeySettings.Calibration);
        var measurementKey = DisplayHotkey(_hotkeySettings.Measurement);
        var clickThroughKey = DisplayHotkey(_hotkeySettings.ToggleClickThrough);
        HideButton.ToolTip = $"隐藏悬浮窗；再次按 {showKey} 恢复";
        ClickThroughButton.ToolTip = $"穿透后鼠标交给游戏，再次按 {clickThroughKey} 返回编辑";
        HotkeyHintText.Text =
            $"{showKey} 显示/隐藏 · {calibrationKey} 校准 · {measurementKey} 测距 · " +
            $"{clickThroughKey} 穿透 · 切换应用自动最小化";

        ModeBadgeText.Text = isCalibrating
            ? "校准中"
            : isMeasuring
                ? "测距中"
                : _clickThrough
                    ? "穿透中"
                    : "查看";
        ModeBadge.Background = BrushFrom(
            isMeasuring ? "#E7F2FF" :
            isCalibrating ? "#FFF3DF" :
            _clickThrough ? "#F0ECFF" : "#E8F1F7FC");
        ModeBadgeText.Foreground = BrushFrom(
            isMeasuring ? "#0768D7" :
            isCalibrating ? "#A65C00" :
            _clickThrough ? "#5A47C8" : "#40576D");

        if (isCalibrating)
        {
            var length = OverlayCanvas.DraftPixelLength;
            CalibrationBadgeText.Text = "草稿未应用";
            CalibrationBadge.Background = BrushFrom("#FFF1D8");
            CalibrationBadgeText.Foreground = BrushFrom("#9A5A00");
            MiniStatusText.Text =
                $"校准草稿 · {length:0.#} px · 600 米固定";
        }
        else if (calibration is not null && _manager.CanMeasure)
        {
            CalibrationBadgeText.Text = "已校准 600 米";
            CalibrationBadge.Background = BrushFrom("#E7F8EF");
            CalibrationBadgeText.Foreground = BrushFrom("#18794A");
            var shortMode = _clickThrough ? "穿透" : isMeasuring ? "测距" : "就绪";
            var currentResult = hasSelection
                ? $" · #{_selectedNumber} {_manager.Find(_selectedNumber!.Value)!.Calculate(calibration).StraightMeters:0} 米"
                : string.Empty;
            MiniStatusText.Text =
                $"{shortMode} · 600 米 = {calibration.PixelLength:0.#} px{currentResult}" +
                (_clickThrough ? $" · {clickThroughKey} 返回" : string.Empty);
        }
        else
        {
            CalibrationBadgeText.Text = "未校准";
            CalibrationBadge.Background = BrushFrom("#FFF1E8");
            CalibrationBadgeText.Foreground = BrushFrom("#A34F12");
            MiniStatusText.Text =
                $"{(_clickThrough ? "穿透" : "编辑")} · 未校准" +
                (_clickThrough ? $" · {clickThroughKey} 返回" : $" · {calibrationKey} 校准");
        }

        if (hasSelection && calibration is not null)
        {
            var record = _manager.Find(_selectedNumber!.Value)!;
            var result = record.Calculate(calibration);
            MeasurementStatusText.Text =
                $"#{record.Number} · {result.StraightMeters:0.0} 米";
        }
        else
        {
            MeasurementStatusText.Text =
                calibration is null
                    ? string.Empty
                    : $"{calibration.MetersPerPixel:0.#####} 米/px";
        }

        var source = _activeSpace is null
            ? "坐标空间未就绪"
            : $"{_activeSpace.DisplayName} · {_activeSpace.Bounds.Width:0}×{_activeSpace.Bounds.Height:0} px";
        var inputState = !OverlayCanvas.CanInteract
            ? "游戏窗口不可用"
            : _clickThrough
                ? "鼠标穿透：开"
                : "鼠标穿透：关";
        CoordinateStatusText.Text =
            $"{source} · {inputState} · {(_normalMode ? "普通窗口" : "透明置顶")}";

        ApplyPanelState();
    }

    private void SetStatus(string message, bool isWarning = false, bool showToast = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = isWarning
            ? BrushFrom("#B45309")
            : BrushFrom("#40515F");
        if (showToast)
        {
            ShowToast(message, isWarning ? 5000 : 3200);
        }
    }

    private void ShowToast(string message, int milliseconds)
    {
        ToastText.Text = message;
        TransientToast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _toastTimer.Start();
    }

    private static SolidColorBrush BrushFrom(string color) =>
        new((Color)ColorConverter.ConvertFromString(color));

    private static string DisplayHotkey(string key) =>
        HotkeyGesture.TryParse(key, out var gesture)
            ? gesture.ToDisplayString()
            : "未设置";
}
