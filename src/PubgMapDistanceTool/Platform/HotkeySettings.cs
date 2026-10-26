using System.IO;
using System.Text.Json;
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
    public string ToggleWindow { get; init; } = "F6";

    public string Calibration { get; init; } = "F7";

    public string Measurement { get; init; } = "F8";

    public string ToggleClickThrough { get; init; } = "F9";

    public static HotkeySettings Default => new();

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
        Key.System,
        Key.LeftCtrl,
        Key.RightCtrl,
        Key.LeftAlt,
        Key.RightAlt,
        Key.LeftShift,
        Key.RightShift,
        Key.LWin,
        Key.RWin
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
            return settings is { } loaded && loaded.Validate().Count == 0
                ? loaded
                : HotkeySettings.Default;
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
