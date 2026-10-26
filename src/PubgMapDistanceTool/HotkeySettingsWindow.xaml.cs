using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PubgMapDistanceTool.Platform;

namespace PubgMapDistanceTool;

public partial class HotkeySettingsWindow : Window
{
    private static readonly IReadOnlyList<KeyOption> AvailableKeys =
    [
        new(Key.None, "未设置"),
        .. HotkeyKeyCatalog.AvailableKeys.Select(key => new KeyOption(key, HotkeyKeyCatalog.GetDisplayName(key)))
    ];

    public HotkeySettingsWindow(HotkeySettings current)
    {
        InitializeComponent();
        ConfigureRow(
            current.ToggleWindow,
            ToggleCtrl,
            ToggleAlt,
            ToggleShift,
            ToggleWin,
            ToggleKeyCombo);
        ConfigureRow(
            current.Calibration,
            CalibrationCtrl,
            CalibrationAlt,
            CalibrationShift,
            CalibrationWin,
            CalibrationKeyCombo);
        ConfigureRow(
            current.Measurement,
            MeasurementCtrl,
            MeasurementAlt,
            MeasurementShift,
            MeasurementWin,
            MeasurementKeyCombo);
        ConfigureRow(
            current.ToggleClickThrough,
            ClickThroughCtrl,
            ClickThroughAlt,
            ClickThroughShift,
            ClickThroughWin,
            ClickThroughKeyCombo);
        Result = current;
    }

    public HotkeySettings Result { get; private set; }

    private static void ConfigureRow(
        string binding,
        CheckBox ctrl,
        CheckBox alt,
        CheckBox shift,
        CheckBox win,
        ComboBox keyCombo)
    {
        if (!HotkeyGesture.TryParse(binding, out var gesture))
        {
            gesture = new HotkeyGesture(ModifierKeys.None, Key.None);
        }

        ctrl.IsChecked = gesture.Modifiers.HasFlag(ModifierKeys.Control);
        alt.IsChecked = gesture.Modifiers.HasFlag(ModifierKeys.Alt);
        shift.IsChecked = gesture.Modifiers.HasFlag(ModifierKeys.Shift);
        win.IsChecked = gesture.Modifiers.HasFlag(ModifierKeys.Windows);
        keyCombo.ItemsSource = AvailableKeys;
        keyCombo.DisplayMemberPath = nameof(KeyOption.Display);
        keyCombo.SelectedItem = AvailableKeys.First(option => option.Key == gesture.Key);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = new HotkeySettings
        {
            ToggleWindow = BuildBinding(ToggleCtrl, ToggleAlt, ToggleShift, ToggleWin, ToggleKeyCombo),
            Calibration = BuildBinding(CalibrationCtrl, CalibrationAlt, CalibrationShift, CalibrationWin, CalibrationKeyCombo),
            Measurement = BuildBinding(MeasurementCtrl, MeasurementAlt, MeasurementShift, MeasurementWin, MeasurementKeyCombo),
            ToggleClickThrough = BuildBinding(ClickThroughCtrl, ClickThroughAlt, ClickThroughShift, ClickThroughWin, ClickThroughKeyCombo)
        };
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, errors),
                "快捷键无效",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var hasRiskySingleKey = settings.Bindings
            .Select(item => HotkeyGesture.TryParse(item.Binding, out var gesture)
                ? gesture
                : new HotkeyGesture(ModifierKeys.None, Key.None))
            .Any(gesture =>
                !gesture.IsEmpty &&
                gesture.Modifiers == ModifierKeys.None &&
                gesture.Key is < Key.F1 or > Key.F24);
        if (hasRiskySingleKey)
        {
            var result = MessageBox.Show(
                this,
                "你设置了不带 Ctrl / Alt / Shift / Win 的普通按键。它会在全局抢占该按键，可能影响游戏或正常输入。仍要保存吗？",
                "单键快捷键风险",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        Result = settings;
        DialogResult = true;
    }

    private static string BuildBinding(
        CheckBox ctrl,
        CheckBox alt,
        CheckBox shift,
        CheckBox win,
        ComboBox keyCombo)
    {
        var key = keyCombo.SelectedItem is KeyOption option ? option.Key : Key.None;
        if (key == Key.None)
        {
            return "None";
        }

        var modifiers = ModifierKeys.None;
        if (ctrl.IsChecked == true)
        {
            modifiers |= ModifierKeys.Control;
        }

        if (alt.IsChecked == true)
        {
            modifiers |= ModifierKeys.Alt;
        }

        if (shift.IsChecked == true)
        {
            modifiers |= ModifierKeys.Shift;
        }

        if (win.IsChecked == true)
        {
            modifiers |= ModifierKeys.Windows;
        }

        return new HotkeyGesture(modifiers, key).ToStorageString();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private sealed record KeyOption(Key Key, string Display);
}
