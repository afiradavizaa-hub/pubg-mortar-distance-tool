using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;

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
    private readonly List<HotkeyBinding> _bindings = [];
    private readonly HashSet<int> _keysDown = [];
    private Dispatcher? _dispatcher;
    private NativeMethods.LowLevelKeyboardProc? _hookCallback;
    private IntPtr _keyboardHook;

    public IReadOnlyList<string> Register(HotkeySettings settings)
    {
        UnregisterAll();
        _dispatcher = Dispatcher.CurrentDispatcher;
        var errors = new List<string>();
        AddBinding(settings.ToggleWindow, HotkeyAction.ToggleWindow, errors);
        AddBinding(settings.Calibration, HotkeyAction.Calibration, errors);
        AddBinding(settings.Measurement, HotkeyAction.Measurement, errors);
        AddBinding(settings.ToggleClickThrough, HotkeyAction.ToggleClickThrough, errors);

        if (_bindings.Count > 0)
        {
            _hookCallback = KeyboardHookCallback;
            _keyboardHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WhKeyboardLl,
                _hookCallback,
                NativeMethods.GetModuleHandle(null),
                0);
            if (_keyboardHook == IntPtr.Zero)
            {
                _bindings.Clear();
                errors.Add($"全局快捷键监听失败：{OverlayWindowController.DescribeWin32Error()}");
            }
        }

        return errors;
    }

    public bool HandleMessage(int message, IntPtr wParam, IntPtr lParam)
    {
        // Kept for Windows message-hook compatibility. Keyboard shortcuts are
        // now observed by the low-level hook below and never consume input.
        return false;
    }

    public event EventHandler<HotkeyAction>? ActionRequested;

    public bool IsRegistered(HotkeyAction action) =>
        _bindings.Any(binding => binding.Action == action);

    public void UnregisterAll()
    {
        _keysDown.Clear();
        _bindings.Clear();
        if (_keyboardHook == IntPtr.Zero)
        {
            return;
        }

        _ = NativeMethods.UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook = IntPtr.Zero;
        _hookCallback = null;
    }

    public static bool IsSupportedKey(string key)
        => HotkeyGesture.TryParse(key, out _);

    private void AddBinding(string keyName, HotkeyAction action, List<string> errors)
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

        var virtualKey = KeyInterop.VirtualKeyFromKey(gesture.Key);
        if (virtualKey != 0)
        {
            _bindings.Add(new HotkeyBinding(action, virtualKey, gesture.Modifiers));
        }
    }

    private IntPtr KeyboardHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            try
            {
                var message = wParam.ToInt32();
                var isKeyDown =
                    message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
                var isKeyUp =
                    message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;
                if (isKeyDown || isKeyUp)
                {
                    var input = Marshal.PtrToStructure<NativeMethods.LowLevelKeyboardInput>(lParam);
                    var virtualKey = checked((int)input.VirtualKeyCode);
                    if (isKeyUp)
                    {
                        _keysDown.Remove(virtualKey);
                    }
                    else if (_keysDown.Add(virtualKey))
                    {
                        var binding = _bindings.FirstOrDefault(candidate =>
                            candidate.VirtualKey == virtualKey &&
                            ModifiersMatch(candidate.Modifiers, virtualKey));
                        if (binding is not null)
                        {
                            _dispatcher?.BeginInvoke(
                                DispatcherPriority.Input,
                                new Action(() => ActionRequested?.Invoke(this, binding.Action)));
                        }
                    }
                }
            }
            catch
            {
                // A hook must never break system input. Errors only disable this
                // trigger; the key event is still forwarded to the game below.
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private static bool ModifiersMatch(ModifierKeys required, int primaryVirtualKey)
    {
        var primaryIsControl = primaryVirtualKey is
            NativeMethods.VkLControl or NativeMethods.VkRControl;
        var primaryIsShift = primaryVirtualKey is
            NativeMethods.VkLShift or NativeMethods.VkRShift;
        var primaryIsAlt = primaryVirtualKey is
            NativeMethods.VkLAlt or NativeMethods.VkRAlt;
        var primaryIsWin = primaryVirtualKey is
            NativeMethods.VkLWin or NativeMethods.VkRWin;

        var controlPressed = !primaryIsControl && IsPressed(NativeMethods.VkControl);
        var shiftPressed = !primaryIsShift && IsPressed(NativeMethods.VkShift);
        var altPressed = !primaryIsAlt && IsPressed(NativeMethods.VkAlt);
        var winPressed =
            !primaryIsWin &&
            (IsPressed(NativeMethods.VkLWin) || IsPressed(NativeMethods.VkRWin));

        return controlPressed == required.HasFlag(ModifierKeys.Control) &&
               shiftPressed == required.HasFlag(ModifierKeys.Shift) &&
               altPressed == required.HasFlag(ModifierKeys.Alt) &&
               winPressed == required.HasFlag(ModifierKeys.Windows);
    }

    private static bool IsPressed(int virtualKey) =>
        (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private sealed record HotkeyBinding(
        HotkeyAction Action,
        int VirtualKey,
        ModifierKeys Modifiers);
}
