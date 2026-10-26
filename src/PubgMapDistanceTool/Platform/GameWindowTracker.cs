using System.Diagnostics;
using System.Text;
using PubgMapDistanceTool.Core.Coordinates;

namespace PubgMapDistanceTool.Platform;

public sealed record GameWindowSnapshot(
    IntPtr Handle,
    int ProcessId,
    string ProcessName,
    string WindowTitle,
    PixelRect ClientBounds,
    uint Dpi,
    string Identity);

public sealed class GameWindowTracker
{
    public bool TryFind(out GameWindowSnapshot snapshot)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var candidates = new List<Candidate>();
        NativeMethods.EnumWindows(
            (handle, _) =>
            {
                if (handle == IntPtr.Zero ||
                    !NativeMethods.IsWindowVisible(handle) ||
                    NativeMethods.IsIconic(handle))
                {
                    return true;
                }

                NativeMethods.GetWindowThreadProcessId(handle, out var processId);
                if (processId == 0 || processId == Environment.ProcessId)
                {
                    return true;
                }

                var processName = TryGetProcessName((int)processId);
                var title = ReadWindowText(handle);
                if (!LooksLikePubg(processName))
                {
                    return true;
                }

                if (!TryGetClientBounds(handle, out var bounds) || !bounds.IsValid)
                {
                    return true;
                }

                candidates.Add(new Candidate(
                    handle,
                    (int)processId,
                    processName,
                    title,
                    bounds,
                    handle == foreground,
                    bounds.Width * bounds.Height));
                return true;
            },
            IntPtr.Zero);

        var selected = candidates
            .OrderByDescending(candidate => candidate.IsForeground)
            .ThenByDescending(candidate => candidate.Area)
            .FirstOrDefault();
        if (selected is null)
        {
            snapshot = default!;
            return false;
        }

        var dpi = Math.Max(96u, NativeMethods.GetDpiForWindow(selected.Handle));
        var identity =
            $"game:{selected.Handle.ToInt64():X}:{selected.ProcessId}:" +
            $"{Math.Round(selected.Bounds.Width):0}x{Math.Round(selected.Bounds.Height):0}:{dpi}";
        snapshot = new GameWindowSnapshot(
            selected.Handle,
            selected.ProcessId,
            selected.ProcessName,
            selected.Title,
            selected.Bounds,
            dpi,
            identity);
        return true;
    }

    public static CoordinateSpace CreateGameSpace(GameWindowSnapshot game) =>
        new(
            game.Identity,
            game.ClientBounds,
            CoordinateSpaceKind.GameClient,
            $"PUBG 客户区 · {game.ProcessName}");

    public static CoordinateSpace CreateDesktopSpace()
    {
        var x = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        var y = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        var width = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen));
        var height = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen));
        var bounds = new PixelRect(x, y, width, height);
        return new CoordinateSpace(
            $"desktop:{x},{y},{width}x{height}",
            bounds,
            CoordinateSpaceKind.DesktopFallback,
            "桌面坐标（未检测到 PUBG）");
    }

    private static bool LooksLikePubg(string processName)
    {
        var normalizedProcess = processName.Trim();
        return normalizedProcess.StartsWith("TslGame", StringComparison.OrdinalIgnoreCase) ||
               normalizedProcess.Contains("Battlegrounds", StringComparison.OrdinalIgnoreCase);
    }

    private static string TryGetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static string ReadWindowText(IntPtr handle)
    {
        var builder = new StringBuilder(512);
        _ = NativeMethods.GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static bool TryGetClientBounds(IntPtr handle, out PixelRect bounds)
    {
        if (!NativeMethods.GetClientRect(handle, out var rect))
        {
            bounds = default;
            return false;
        }

        var origin = default(NativeMethods.Point);
        if (!NativeMethods.ClientToScreen(handle, ref origin))
        {
            bounds = default;
            return false;
        }

        bounds = new PixelRect(
            origin.X,
            origin.Y,
            Math.Max(0, rect.Right - rect.Left),
            Math.Max(0, rect.Bottom - rect.Top));
        return bounds.IsValid;
    }

    private sealed record Candidate(
        IntPtr Handle,
        int ProcessId,
        string ProcessName,
        string Title,
        PixelRect Bounds,
        bool IsForeground,
        double Area);
}
