using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace PubgMapDistanceTool.Platform;

public sealed record HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    public bool IsEmpty => Key == Key.None;

    public string ToStorageString()
    {
        if (IsEmpty)
        {
            return "None";
        }

        var parts = new List<string>(5);
        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(Key.ToString());
        return string.Join('+', parts);
    }

    public string ToDisplayString()
    {
        if (IsEmpty)
        {
            return "未设置";
        }

        var parts = new List<string>(5);
        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(HotkeyKeyCatalog.GetDisplayName(Key));
        return string.Join('+', parts);
    }

    public static bool TryParse(string? value, out HotkeyGesture gesture)
    {
        gesture = new HotkeyGesture(ModifierKeys.None, Key.None);
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "None", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var modifiers = ModifierKeys.None;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            switch (parts[index].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    return false;
            }
        }

        if (!Enum.TryParse<Key>(parts[^1], ignoreCase: true, out var key) ||
            !HotkeyKeyCatalog.IsUsableKey(key))
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }
}

public sealed record HotkeySettings
{
    public string ToggleWindow { get; init; } = "Up";

    public string Calibration { get; init; } = "None";

    public string Measurement { get; init; } = "None";

    public string ToggleClickThrough { get; init; } = "Down";

    public static HotkeySettings Default => new();

    [JsonIgnore]
    public IReadOnlyList<(string Name, string Binding)> Bindings =>
    [
        ("显示/隐藏", ToggleWindow),
        ("校准", Calibration),
        ("测距", Measurement),
        ("鼠标穿透", ToggleClickThrough)
    ];

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        foreach (var (name, value) in Bindings)
        {
            if (!HotkeyGesture.TryParse(value, out _))
            {
                errors.Add($"{name}快捷键无效：{value}");
            }
        }

        var canonicalBindings = new List<(string Name, string Canonical)>();
        foreach (var (name, value) in Bindings)
        {
            if (HotkeyGesture.TryParse(value, out var gesture) && !gesture.IsEmpty)
            {
                canonicalBindings.Add((name, gesture.ToStorageString()));
            }
        }

        var duplicates = canonicalBindings
            .GroupBy(item => item.Canonical, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        errors.AddRange(duplicates.Select(value => $"快捷键 {value} 被重复使用。"));
        return errors;
    }
}

public static class HotkeyKeyCatalog
{
    private static readonly HashSet<Key> ExcludedKeys =
    [
        Key.None,
        Key.DeadCharProcessed,
        Key.ImeProcessed,
        Key.System
    ];

    public static IReadOnlyList<Key> AvailableKeys { get; } =
        Enum.GetValues<Key>()
            .Where(IsUsableKey)
            .Distinct()
            .OrderBy(KeySortOrder)
            .ThenBy(GetDisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    public static bool IsUsableKey(Key key) =>
        key != Key.None &&
        !ExcludedKeys.Contains(key) &&
        (uint)KeyInterop.VirtualKeyFromKey(key) != 0;

    public static string GetDisplayName(Key key) =>
        key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => $"小键盘 {(int)key - (int)Key.NumPad0}",
            Key.Back => "Backspace",
            Key.Capital => "Caps Lock",
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Space => "Space",
            Key.Prior => "Page Up",
            Key.Next => "Page Down",
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemTilde => "`",
            Key.LeftCtrl => "左 Ctrl",
            Key.RightCtrl => "右 Ctrl",
            Key.LeftAlt => "左 Alt",
            Key.RightAlt => "右 Alt",
            Key.LeftShift => "左 Shift",
            Key.RightShift => "右 Shift",
            Key.LWin => "左 Win",
            Key.RWin => "右 Win",
            Key.MediaNextTrack => "下一曲",
            Key.MediaPreviousTrack => "上一曲",
            Key.MediaPlayPause => "播放 / 暂停",
            Key.MediaStop => "停止播放",
            Key.VolumeMute => "静音",
            Key.VolumeDown => "音量减",
            Key.VolumeUp => "音量加",
            Key.BrowserBack => "浏览器后退",
            Key.BrowserForward => "浏览器前进",
            Key.BrowserRefresh => "浏览器刷新",
            Key.BrowserStop => "浏览器停止",
            Key.BrowserSearch => "浏览器搜索",
            Key.BrowserFavorites => "浏览器收藏夹",
            Key.BrowserHome => "浏览器主页",
            Key.LaunchMail => "打开邮件",
            Key.SelectMedia => "选择媒体",
            Key.LaunchApplication1 => "应用键 1",
            Key.LaunchApplication2 => "应用键 2",
            _ => key.ToString()
        };

    private static int KeySortOrder(Key key) =>
        key switch
        {
            >= Key.F1 and <= Key.F24 => 0,
            >= Key.A and <= Key.Z => 1,
            >= Key.D0 and <= Key.D9 => 2,
            >= Key.NumPad0 and <= Key.NumPad9 => 3,
            _ => 4
        };
}

public static class HotkeySettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PubgMapDistanceTool",
            "settings.json");

    private static bool IsLegacyFunctionKeyDefault(HotkeySettings settings) =>
        string.Equals(settings.ToggleWindow, "F6", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(settings.Calibration, "F7", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(settings.Measurement, "F8", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(settings.ToggleClickThrough, "F9", StringComparison.OrdinalIgnoreCase);

    public static HotkeySettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return HotkeySettings.Default;
            }

            var settings = JsonSerializer.Deserialize<HotkeySettings>(
                File.ReadAllText(SettingsPath),
                JsonOptions);
            if (settings is not { } loaded || loaded.Validate().Count > 0)
            {
                return HotkeySettings.Default;
            }

            if (IsLegacyFunctionKeyDefault(loaded))
            {
                var migrated = HotkeySettings.Default;
                try
                {
                    Save(migrated);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Keep the migrated in-memory defaults even if the settings
                    // file cannot be rewritten.
                }

                return migrated;
            }

            return loaded;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return HotkeySettings.Default;
        }
    }

    public static void Save(HotkeySettings settings)
    {
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            SettingsPath,
            JsonSerializer.Serialize(settings, JsonOptions));
    }
}
