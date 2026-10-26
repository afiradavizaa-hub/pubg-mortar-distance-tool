using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PubgMapDistanceTool.Core.Coordinates;

namespace PubgMapDistanceTool.Platform;

public static class OverlayWindowController
{
    public static IntPtr GetHandle(Window window) =>
        new WindowInteropHelper(window).Handle;

    public static PixelRect GetClientBounds(Window window)
    {
        var handle = GetHandle(window);
        if (handle == IntPtr.Zero || !NativeMethods.GetClientRect(handle, out var rect))
        {
            return default;
        }

        var origin = default(NativeMethods.Point);
        if (!NativeMethods.ClientToScreen(handle, ref origin))
        {
            return default;
        }

        return new PixelRect(
            origin.X,
            origin.Y,
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top));
    }

    public static void SetClickThrough(Window window, bool enabled)
    {
        var handle = GetHandle(window);
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var extendedStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        if (enabled)
        {
            extendedStyle |= NativeMethods.WsExTransparent;
        }
        else
        {
            extendedStyle &= ~NativeMethods.WsExTransparent;
        }

        NativeMethods.SetWindowLongPtr(
            handle,
            NativeMethods.GwlExStyle,
            new IntPtr(extendedStyle));
        _ = NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove |
            NativeMethods.SwpNoSize |
            NativeMethods.SwpNoZOrder |
            NativeMethods.SwpNoActivate |
            NativeMethods.SwpFrameChanged);
    }

    public static void SnapToBounds(Window window, PixelRect bounds, bool topmost)
    {
        if (!bounds.IsValid)
        {
            return;
        }

        var handle = GetHandle(window);
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _ = NativeMethods.SetWindowPos(
            handle,
            topmost ? NativeMethods.HwndTopmost : IntPtr.Zero,
            (int)Math.Round(bounds.X),
            (int)Math.Round(bounds.Y),
            (int)Math.Round(bounds.Width),
            (int)Math.Round(bounds.Height),
            NativeMethods.SwpNoActivate |
            (topmost ? 0u : NativeMethods.SwpNoZOrder));
    }

    public static string DescribeWin32Error() =>
        new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;
}
