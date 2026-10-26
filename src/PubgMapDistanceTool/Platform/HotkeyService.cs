using System.Windows.Input;
using System.Windows.Interop;

namespace PubgMapDistanceTool.Platform;

public enum HotkeyAction
{
    ToggleWindow = 1,
    Calibration = 2,
    Measurement = 3,
    ToggleClickThrough = 4
}

public sealed class HotkeyService
{
    private readonly Dictionary<int, HotkeyAction> _registered = [];
    private IntPtr _handle;

    public IReadOnlyList<string> Register(HotkeySettings settings, IntPtr handle)
    {
        UnregisterAll();
        _handle = handle;
        var errors = new List<string>();
        RegisterOne(settings.ToggleWindow, HotkeyAction.ToggleWindow, errors);
        RegisterOne(settings.Calibration, HotkeyAction.Calibration, errors);
        RegisterOne(settings.Measurement, HotkeyAction.Measurement, errors);
        RegisterOne(settings.ToggleClickThrough, HotkeyAction.ToggleClickThrough, errors);
        return errors;
    }

    public bool HandleMessage(int message, IntPtr wParam, IntPtr lParam)
    {
        if (message != 0x0312 || !_registered.TryGetValue(wParam.ToInt32(), out var action))
        {
            return false;
        }

        ActionRequested?.Invoke(this, action);
        return true;
    }

    public event EventHandler<HotkeyAction>? ActionRequested;

    public bool IsRegistered(HotkeyAction action) =>
        _registered.ContainsValue(action);

    public void UnregisterAll()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        foreach (var id in _registered.Keys)
        {
            _ = NativeMethods.UnregisterHotKey(_handle, id);
        }

        _registered.Clear();
    }

    public static bool IsSupportedKey(string key)
        => HotkeyGesture.TryParse(key, out _);

    private void RegisterOne(string keyName, HotkeyAction action, List<string> errors)
    {
        if (!HotkeyGesture.TryParse(keyName, out var gesture))
        {
            errors.Add($"{action} 快捷键无效：{keyName}");
            return;
        }

        if (gesture.IsEmpty)
        {
            return;
        }

        var modifiers = NativeMethods.ModNoRepeat;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= NativeMethods.ModControl;
        }

        if (gesture.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= NativeMethods.ModAlt;
        }

        if (gesture.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= NativeMethods.ModShift;
        }

        if (gesture.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            modifiers |= NativeMethods.ModWin;
        }

        var id = (int)action + 0x4200;
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(gesture.Key);
        if (virtualKey == 0 ||
            !NativeMethods.RegisterHotKey(_handle, id, modifiers, virtualKey))
        {
            errors.Add($"{gesture.ToDisplayString()} 注册失败，可能已被系统或其他程序占用。");
            return;
        }

        _registered[id] = action;
    }
}
