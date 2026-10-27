using System.IO;
using System.Text;
using System.Windows.Input;
using PubgMapDistanceTool.Core.CalibrationModel;
using PubgMapDistanceTool.Core.Coordinates;
using PubgMapDistanceTool.Core.Export;
using PubgMapDistanceTool.Core.Geometry;
using PubgMapDistanceTool.Core.Measurements;
using PubgMapDistanceTool.Platform;

namespace PubgMapDistanceTool.Tests;

internal static class Program
{
    private const double Tolerance = 0.000001;

    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("测试一：720 像素对应固定 600 米", TestCalibration600),
            ("测试二：不同显示比例独立计算", TestDifferentCoordinateScales),
            ("测试三：水平、垂直与对角线", TestDiagonalDistance),
            ("测试四：高 DPI 双向坐标转换", TestHighDpiMapping),
            ("测试五：移动悬浮窗不改变游戏坐标", TestOverlayMoveStability),
            ("测试六：校准草稿不提前应用", TestCalibrationDraft),
            ("测试七：游戏尺寸改变后清除旧坐标数据", TestCoordinateSpaceInvalidation),
            ("测试八：新增、编辑、删除、清除与撤销", TestMeasurementManagement),
            ("测试九：CSV UTF-8 BOM 与字段", TestCsvExport),
            ("测试十：全键盘组合快捷键解析", TestHotkeyGestures),
            ("边界：零长度和非法坐标被拒绝", TestInvalidCalibration),
            ("边界：校准绑定坐标空间身份", TestCoordinateIdentity),
            ("坐标：游戏客户区与桌面回退容器有效", TestCoordinateSpaceKinds)
        };

        var passed = 0;
        foreach (var (name, run) in tests)
        {
            try
            {
                run();
                passed++;
                Console.WriteLine($"[通过] {name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[失败] {name}");
                Console.WriteLine($"       {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"结果：{passed}/{tests.Length} 项通过");
        return passed == tests.Length ? 0 : 1;
    }

    private static void TestCalibration600()
    {
        var calibration = CalibrationAt(720);
        AssertNear(720, calibration.PixelLength);
        AssertNear(600, calibration.BenchmarkMeters);
        AssertNear(600d / 720d, calibration.MetersPerPixel);

        var sameLength = calibration.Measure(new MapPoint(10, 10), new MapPoint(730, 10));
        AssertNear(600, sameLength.StraightMeters);

        var halfLength = calibration.Measure(new MapPoint(0, 0), new MapPoint(360, 0));
        AssertNear(300, halfLength.StraightMeters);
    }

    private static void TestDifferentCoordinateScales()
    {
        var gameA = new Calibration(new MapPoint(0, 0), new MapPoint(600, 0), "game-A");
        var gameB = new Calibration(new MapPoint(0, 0), new MapPoint(900, 0), "game-B");
        var pixelDistance = new MapPoint(450, 0);

        AssertNear(450, gameA.Measure(new MapPoint(0, 0), pixelDistance).StraightMeters);
        AssertNear(300, gameB.Measure(new MapPoint(0, 0), pixelDistance).StraightMeters);
        AssertNotNear(gameA.MetersPerPixel, gameB.MetersPerPixel);
    }

    private static void TestDiagonalDistance()
    {
        var result = CalibrationAt(720).Measure(new MapPoint(20, 30), new MapPoint(140, 150));
        AssertNear(100, result.HorizontalMeters);
        AssertNear(100, result.VerticalMeters);
        AssertNear(141.4213562373, result.StraightMeters);
    }

    private static void TestHighDpiMapping()
    {
        var map = new UnifiedCoordinateMap(
            new PixelRect(1920, 120, 2560, 1440),
            new PixelRect(1800, 80, 2560, 1440),
            dpiScaleX: 1.5,
            dpiScaleY: 1.5);
        var gamePoint = new MapPoint(1250.5, 640.25);
        var overlayDip = map.CoordinateToOverlayDip(gamePoint);
        var roundTrip = map.OverlayDipToCoordinate(new ScreenPoint(overlayDip.X, overlayDip.Y));

        AssertNear(913.6666666667, overlayDip.X);
        AssertNear(453.5, overlayDip.Y);
        AssertNear(gamePoint.X, roundTrip.X);
        AssertNear(gamePoint.Y, roundTrip.Y);

        var screen = map.CoordinateToScreen(gamePoint);
        var screenRoundTrip = map.ScreenToCoordinate(new PixelPoint(screen.X, screen.Y));
        AssertNear(gamePoint.X, screenRoundTrip.X);
        AssertNear(gamePoint.Y, screenRoundTrip.Y);
    }

    private static void TestOverlayMoveStability()
    {
        var coordinateSpace = new PixelRect(100, 50, 1920, 1080);
        var point = new MapPoint(800, 450);
        var before = new UnifiedCoordinateMap(
            coordinateSpace,
            new PixelRect(100, 50, 1920, 1080),
            1,
            1);
        var after = new UnifiedCoordinateMap(
            coordinateSpace,
            new PixelRect(300, 180, 1920, 1080),
            1,
            1);

        var screenBefore = before.CoordinateToScreen(point);
        var screenAfter = after.CoordinateToScreen(point);
        AssertNear(screenBefore.X, screenAfter.X);
        AssertNear(screenBefore.Y, screenAfter.Y);
        AssertNear(600, after.CoordinateToOverlayDip(point).X);
        AssertNear(320, after.CoordinateToOverlayDip(point).Y);
    }

    private static void TestCalibrationDraft()
    {
        var manager = new MeasurementManager();
        manager.SetCoordinateSpace("game-A");
        var official = new Calibration(new MapPoint(0, 0), new MapPoint(720, 0), "game-A");
        manager.ConfirmCalibration(official);
        manager.AddMeasurement(new MapPoint(0, 0), new MapPoint(720, 0));

        var draft = new Calibration(new MapPoint(0, 0), new MapPoint(900, 0), "game-A");
        AssertNear(720, manager.Calibration!.PixelLength);
        AssertNear(600, manager.Records[0].Calculate(manager.Calibration).StraightMeters);
        AssertNear(900, draft.PixelLength);
        AssertNear(600, draft.BenchmarkMeters);

        manager.ConfirmCalibration(draft);
        AssertNear(480, manager.Records[0].Calculate(manager.Calibration!).StraightMeters);
    }

    private static void TestCoordinateSpaceInvalidation()
    {
        var manager = new MeasurementManager();
        manager.SetCoordinateSpace("game:WIDTH=1920");
        manager.ConfirmCalibration(new Calibration(new MapPoint(0, 0), new MapPoint(600, 0), "game:WIDTH=1920"));
        manager.AddMeasurement(new MapPoint(0, 0), new MapPoint(300, 0));

        manager.SetCoordinateSpace("game:WIDTH=2560");
        AssertEqual(null, manager.Calibration);
        AssertEqual(0, manager.Records.Count);
        AssertEqual(false, manager.CanMeasure);
        AssertEqual(false, manager.CanUndo);
    }

    private static void TestMeasurementManagement()
    {
        var manager = new MeasurementManager();
        manager.SetCoordinateSpace("game-A");
        manager.ConfirmCalibration(new Calibration(new MapPoint(0, 0), new MapPoint(720, 0), "game-A"));

        var first = manager.AddMeasurement(new MapPoint(10, 10), new MapPoint(370, 10));
        var second = manager.AddMeasurement(new MapPoint(0, 0), new MapPoint(120, 120));
        AssertEqual(1, first.Number);
        AssertEqual(1, second.Number);
        AssertEqual(1, manager.Records.Count);
        AssertNear(120, manager.Records[0].End.X);

        manager.UpdateMeasurementPoints(1, new MapPoint(20, 20), new MapPoint(740, 20));
        AssertNear(20, manager.Find(1)!.Start.X);
        AssertEqual(true, manager.Undo());
        AssertNear(0, manager.Find(1)!.Start.X);

        AssertEqual(true, manager.DeleteMeasurement(1));
        AssertEqual(0, manager.Records.Count);
        AssertEqual(true, manager.Undo());
        AssertEqual(1, manager.Records.Count);
        AssertNear(0, manager.Find(1)!.Start.X);

        AssertEqual(true, manager.ClearMeasurements());
        AssertEqual(0, manager.Records.Count);
        AssertEqual(true, manager.Undo());
        AssertEqual(1, manager.Records.Count);
    }

    private static void TestCsvExport()
    {
        var manager = new MeasurementManager();
        manager.SetCoordinateSpace("game-A");
        var calibration = CalibrationAt(720);
        manager.ConfirmCalibration(calibration);
        manager.AddMeasurement(new MapPoint(12.5, 8), new MapPoint(132.5, 128));

        var expected = CsvExporter.Build(manager.Records, calibration);
        AssertTrue(expected.StartsWith("测量编号,起点 X 坐标", StringComparison.Ordinal));
        AssertTrue(expected.Contains("141.4", StringComparison.Ordinal));

        var path = Path.Combine(Path.GetTempPath(), $"pubg-overlay-distance-{Guid.NewGuid():N}.csv");
        try
        {
            CsvExporter.Write(path, manager.Records, calibration);
            var bytes = File.ReadAllBytes(path);
            AssertTrue(bytes.Length > 3);
            AssertEqual((byte)0xEF, bytes[0]);
            AssertEqual((byte)0xBB, bytes[1]);
            AssertEqual((byte)0xBF, bytes[2]);
            AssertEqual(expected, File.ReadAllText(path, new UTF8Encoding(true)));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void TestHotkeyGestures()
    {
        AssertTrue(HotkeyGesture.TryParse("Ctrl+Alt+M", out var first));
        AssertEqual(ModifierKeys.Control | ModifierKeys.Alt, first.Modifiers);
        AssertEqual(Key.M, first.Key);
        AssertEqual("Ctrl+Alt+M", first.ToStorageString());

        AssertTrue(HotkeyGesture.TryParse("Ctrl+Shift+NumPad4", out var second));
        AssertEqual(ModifierKeys.Control | ModifierKeys.Shift, second.Modifiers);
        AssertEqual(Key.NumPad4, second.Key);

        AssertTrue(HotkeyGesture.TryParse("Win+Space", out var third));
        AssertEqual(ModifierKeys.Windows, third.Modifiers);
        AssertEqual(Key.Space, third.Key);

        AssertEqual(false, HotkeyGesture.TryParse("Ctrl+", out _));
        AssertEqual(false, HotkeyGesture.TryParse("Hyper+A", out _));
        AssertTrue(HotkeyKeyCatalog.AvailableKeys.Contains(Key.A));
        AssertTrue(HotkeyKeyCatalog.AvailableKeys.Contains(Key.NumPad9));
        AssertTrue(HotkeyKeyCatalog.AvailableKeys.Contains(Key.LeftCtrl));
        AssertTrue(HotkeyKeyCatalog.AvailableKeys.Contains(Key.RightAlt));
        AssertTrue(HotkeyKeyCatalog.AvailableKeys.Contains(Key.LeftShift));
        AssertTrue(HotkeyKeyCatalog.AvailableKeys.Contains(Key.RWin));
        AssertTrue(HotkeyKeyCatalog.AvailableKeys.Contains(Key.MediaPlayPause));
        AssertTrue(HotkeyKeyCatalog.AvailableKeys.Contains(Key.VolumeUp));
        AssertTrue(HotkeyKeyCatalog.AvailableKeys.Contains(Key.BrowserBack));
        AssertTrue(HotkeyKeyCatalog.AvailableKeys.Contains(Key.OemClear));

        var duplicate = new HotkeySettings
        {
            ToggleWindow = "Ctrl+Alt+M",
            Calibration = "Alt+Ctrl+M",
            Measurement = "F8",
            ToggleClickThrough = "F9"
        };
        AssertTrue(duplicate.Validate().Any(error => error.Contains("重复", StringComparison.Ordinal)));
    }

    private static void TestInvalidCalibration()
    {
        AssertThrows<ArgumentException>(() =>
            _ = new Calibration(new MapPoint(10, 10), new MapPoint(10, 10), "game-A"));
        AssertThrows<ArgumentException>(() =>
            _ = new Calibration(new MapPoint(double.NaN, 0), new MapPoint(20, 0), "game-A"));
        AssertThrows<ArgumentException>(() =>
            _ = new Calibration(new MapPoint(0, 0), new MapPoint(20, 5), "game-A"));
        AssertThrows<ArgumentException>(() =>
            CalibrationAt(720).Measure(new MapPoint(0, 0), new MapPoint(double.PositiveInfinity, 0)));
    }

    private static void TestCoordinateIdentity()
    {
        var calibration = CalibrationAt(720);
        calibration.EnsureMatchesCoordinateSpace("game-A");
        AssertThrows<InvalidOperationException>(() => calibration.EnsureMatchesCoordinateSpace("game-B"));

        var manager = new MeasurementManager();
        manager.SetCoordinateSpace("game-B");
        AssertThrows<InvalidOperationException>(() => manager.ConfirmCalibration(calibration));
    }

    private static void TestCoordinateSpaceKinds()
    {
        var game = new CoordinateSpace(
            "game:1",
            new PixelRect(100, 50, 1920, 1080),
            CoordinateSpaceKind.GameClient,
            "PUBG 客户区");
        var desktop = new CoordinateSpace(
            "desktop:0",
            new PixelRect(0, 0, 2560, 1440),
            CoordinateSpaceKind.DesktopFallback,
            "桌面");

        AssertTrue(game.IsValid);
        AssertTrue(desktop.IsValid);
        AssertTrue(game.Bounds.Contains(new PixelPoint(100, 50)));
        AssertEqual(false, game.Bounds.Contains(new PixelPoint(99, 50)));
    }

    private static Calibration CalibrationAt(double pixelLength) =>
        new(new MapPoint(0, 0), new MapPoint(pixelLength, 0), "game-A");

    private static void AssertTrue(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("预期条件为 true，但实际为 false。");
        }
    }

    private static void AssertNear(double expected, double actual)
    {
        if (double.IsNaN(expected) || double.IsNaN(actual) || Math.Abs(expected - actual) > Tolerance)
        {
            throw new InvalidOperationException($"预期 {expected:R}，实际 {actual:R}。");
        }
    }

    private static void AssertNotNear(double first, double second)
    {
        if (!double.IsNaN(first) && !double.IsNaN(second) && Math.Abs(first - second) <= Tolerance)
        {
            throw new InvalidOperationException($"两个值不应相等：{first:R}。");
        }
    }

    private static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"预期 {expected}，实际 {actual}。");
        }
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"预期抛出 {typeof(TException).Name}。");
    }
}
