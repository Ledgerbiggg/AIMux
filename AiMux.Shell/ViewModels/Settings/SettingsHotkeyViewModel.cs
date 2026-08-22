using System.Collections.ObjectModel;
using System.Windows.Input;
using AiMux.Common.Config;
using AiMux.Common.Hotkey;
using AiMux.Models;
using AiMux.Shell.Util;
using Prism.Commands;
using Prism.Mvvm;

namespace AiMux.Shell.ViewModels.Settings;

/// <summary>热键设置面板：每个热键一行（动作 + 录制 + 保存）
/// 点击"录制"后监听按键，捕获到组合键自动填入
/// 集中管理所有热键，后续要加动作只需扩展 HotkeyAction 枚举</summary>
public class SettingsHotkeyViewModel : BindableBase
{
    private readonly ConfigService _config;
    private readonly AppSettings _settings;

    /// <summary>所有可用的热键动作（供 DataGrid/ComboBox 绑定）</summary>
    public IReadOnlyList<HotkeyAction> AvailableActions { get; } =
        Enum.GetValues<HotkeyAction>().ToList();

    /// <summary>热键绑定列表（每行可独立录制）</summary>
    public ObservableCollection<HotkeyRowViewModel> Rows { get; } = [];

    public DelegateCommand SaveCommand { get; }
    public DelegateCommand ResetCommand { get; }

    public SettingsHotkeyViewModel(ConfigService config, HotkeyManager hotkeyManager)
    {
        _config = config;
        _settings = config.LoadSettings();

        // 始终显示六行热键（写死动作，可各自录制组合键）：
        // 呼出窗口(Alt+Q，全局) / 折叠侧栏(Alt+E) / 切换尺寸(Alt+W) / 打开设置(Alt+S) /
        // 上一个平台(Alt+←) / 下一个平台(Alt+→)
        // 其中侧栏/尺寸/设置/切换仅在主窗口聚焦时生效；有配置就用配置，否则填默认
        var toggleWindowBinding = _settings.Hotkeys.FirstOrDefault(h => h.Action == HotkeyAction.ToggleWindow)
            ?? new HotkeyBinding { Action = HotkeyAction.ToggleWindow, Modifier = "Alt", Key = "Q" };
        var toggleSizeBinding = _settings.Hotkeys.FirstOrDefault(h => h.Action == HotkeyAction.ToggleSize)
            ?? new HotkeyBinding { Action = HotkeyAction.ToggleSize, Modifier = "Alt", Key = "W" };
        var toggleSidebarBinding = _settings.Hotkeys.FirstOrDefault(h => h.Action == HotkeyAction.ToggleSidebar)
            ?? new HotkeyBinding { Action = HotkeyAction.ToggleSidebar, Modifier = "Alt", Key = "E" };
        var toggleSettingsBinding = _settings.Hotkeys.FirstOrDefault(h => h.Action == HotkeyAction.ToggleSettings)
            ?? new HotkeyBinding { Action = HotkeyAction.ToggleSettings, Modifier = "Alt", Key = "S" };
        var prevPlatformBinding = _settings.Hotkeys.FirstOrDefault(h => h.Action == HotkeyAction.PrevPlatform)
            ?? new HotkeyBinding { Action = HotkeyAction.PrevPlatform, Modifier = "Alt", Key = "Left" };
        var nextPlatformBinding = _settings.Hotkeys.FirstOrDefault(h => h.Action == HotkeyAction.NextPlatform)
            ?? new HotkeyBinding { Action = HotkeyAction.NextPlatform, Modifier = "Alt", Key = "Right" };

        Rows.Add(new HotkeyRowViewModel(toggleWindowBinding));
        Rows.Add(new HotkeyRowViewModel(toggleSizeBinding));
        Rows.Add(new HotkeyRowViewModel(toggleSidebarBinding));
        Rows.Add(new HotkeyRowViewModel(toggleSettingsBinding));
        Rows.Add(new HotkeyRowViewModel(prevPlatformBinding));
        Rows.Add(new HotkeyRowViewModel(nextPlatformBinding));

        SaveCommand = new DelegateCommand(Save);
        ResetCommand = new DelegateCommand(Reset);
    }

    /// <summary>恢复默认值：Alt+Q 窗口(全局)、Alt+E 侧栏、Alt+W 尺寸、Alt+S 设置、Alt+← 上一平台、Alt+→ 下一平台。
    /// 保留六行不清除，方便直接修改</summary>
    private void Reset()
    {
        // 始终保留六行，恢复默认组合键而非清空
        if (Rows.Count == 0)
        {
            Rows.Add(new HotkeyRowViewModel(new HotkeyBinding { Action = HotkeyAction.ToggleWindow }));
            Rows.Add(new HotkeyRowViewModel(new HotkeyBinding { Action = HotkeyAction.ToggleSize }));
            Rows.Add(new HotkeyRowViewModel(new HotkeyBinding { Action = HotkeyAction.ToggleSidebar }));
            Rows.Add(new HotkeyRowViewModel(new HotkeyBinding { Action = HotkeyAction.ToggleSettings }));
            Rows.Add(new HotkeyRowViewModel(new HotkeyBinding { Action = HotkeyAction.PrevPlatform }));
            Rows.Add(new HotkeyRowViewModel(new HotkeyBinding { Action = HotkeyAction.NextPlatform }));
        }
        Rows[0].Modifier = "Alt"; Rows[0].Key = "Q";
        Rows[1].Modifier = "Alt"; Rows[1].Key = "W";
        Rows[2].Modifier = "Alt"; Rows[2].Key = "E";
        Rows[3].Modifier = "Alt"; Rows[3].Key = "S";
        Rows[4].Modifier = "Alt"; Rows[4].Key = "Left";
        Rows[5].Modifier = "Alt"; Rows[5].Key = "Right";
        _ = MessageBoxHelper.Info("已恢复默认：Alt+Q 窗口，Alt+W 尺寸，Alt+E 侧栏，Alt+S 设置，Alt+← 上一平台，Alt+→ 下一平台。");
    }

    /// <summary>保存前逻辑校验：每条需有按键，且同一组合键不能被多个动作重复占用。
    /// 注意：不做真实 RegisterHotKey 试注册——主窗口已注册了这些热键，
    /// 同进程内重复注册必然报"被占用"而阻断保存（即自己与自己的冲突）。</summary>
    private void Save()
    {
        var bindings = Rows.Select(r => r.ToBinding()).ToList();
        var errors = new List<string>();

        // 检查每条都有按键
        foreach (var b in bindings)
        {
            if (string.IsNullOrEmpty(b.Key))
                errors.Add($"{b.Action}：未设置按键");
        }

        // 检查同一组合键（修饰键+按键，忽略大小写）是否分配给多个动作
        var seen = new Dictionary<string, HotkeyAction>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in bindings)
        {
            if (string.IsNullOrEmpty(b.Key)) continue;
            var key = $"{b.Modifier?.Trim()?.ToLowerInvariant()}|{b.Key.Trim().ToLowerInvariant()}";
            if (seen.TryGetValue(key, out var other))
            {
                errors.Add($"{b.Action} 与 {other} 重复占用 {b.Modifier}+{b.Key}");
            }
            else
            {
                seen[key] = b.Action;
            }
        }

        if (errors.Count > 0)
        {
            _ = MessageBoxHelper.Warn(string.Join("；", errors));
            return;
        }

        _settings.Hotkeys = bindings;
        _config.SaveSettings(_settings);
        _ = MessageBoxHelper.Info("热键已保存，立即生效。");
    }
}

/// <summary>单行热键绑定：动作 + 修饰键 + 按键 + 录制状态</summary>
public class HotkeyRowViewModel : BindableBase
{
    public HotkeyBinding Model { get; }

    public HotkeyAction Action
    {
        get => Model.Action;
        set { Model.Action = value; RaisePropertyChanged(); }
    }

    public string Modifier
    {
        get => Model.Modifier;
        set
        {
            Model.Modifier = value ?? "";
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(Display));
        }
    }

    public string Key
    {
        get => Model.Key;
        set
        {
            Model.Key = value ?? "";
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(Display));
        }
    }

    private bool _isRecording;
    public bool IsRecording
    {
        get => _isRecording;
        set => SetProperty(ref _isRecording, value);
    }

    /// <summary>可读显示："Ctrl + Alt + Space"</summary>
    public string Display
    {
        get
        {
            var mod = Modifier.Trim();
            var key = Key.Trim();
            if (string.IsNullOrEmpty(mod) && string.IsNullOrEmpty(key)) return "未设置";
            if (string.IsNullOrEmpty(mod)) return key;
            return $"{mod} + {key}";
        }
    }

    public DelegateCommand RecordCommand { get; }

    public HotkeyRowViewModel(HotkeyBinding model)
    {
        Model = model;
        RecordCommand = new DelegateCommand(ToggleRecording);
    }

    /// <summary>切换录制状态</summary>
    private void ToggleRecording()
    {
        IsRecording = !IsRecording;
    }

    /// <summary>由 View 的 PreviewKeyDown 调用：捕获组合键</summary>
    public void CaptureKey(ModifierKeys modifiers, System.Windows.Input.Key key)
    {
        if (key == System.Windows.Input.Key.Escape)
        {
            IsRecording = false;
            return;
        }

        var nonModifier = GetNonModifierKey(key);
        if (nonModifier == null) return; // 仅修饰键，继续等待

        if (modifiers == ModifierKeys.None)
        {
            // 必须有修饰键，避免单键冲突
            return;
        }

        IsRecording = false;
        Modifier = ModifiersToString(modifiers);
        Key = nonModifier;
    }

    public HotkeyBinding ToBinding() => new() { Action = Action, Modifier = Modifier, Key = Key };

    private static string? GetNonModifierKey(System.Windows.Input.Key key)
    {
        if (key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
            or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin
            or System.Windows.Input.Key.System)
            return null;

        return key switch
        {
            System.Windows.Input.Key.Space => "Space",
            System.Windows.Input.Key.OemTilde => "`",
            System.Windows.Input.Key.OemMinus => "-",
            System.Windows.Input.Key.OemPlus => "=",
            System.Windows.Input.Key.OemOpenBrackets => "[",
            System.Windows.Input.Key.OemCloseBrackets => "]",
            System.Windows.Input.Key.OemPipe => "\\",
            System.Windows.Input.Key.OemSemicolon => ";",
            System.Windows.Input.Key.OemQuotes => "'",
            System.Windows.Input.Key.OemComma => ",",
            System.Windows.Input.Key.OemPeriod => ".",
            System.Windows.Input.Key.OemQuestion => "/",
            System.Windows.Input.Key.Tab => "Tab",
            System.Windows.Input.Key.Enter => "Enter",
            System.Windows.Input.Key.Back => "Back",
            System.Windows.Input.Key.Insert => "Insert",
            System.Windows.Input.Key.Delete => "Delete",
            System.Windows.Input.Key.Home => "Home",
            System.Windows.Input.Key.End => "End",
            System.Windows.Input.Key.PageUp => "PageUp",
            System.Windows.Input.Key.PageDown => "PageDown",
            System.Windows.Input.Key.Left => "Left",
            System.Windows.Input.Key.Right => "Right",
            System.Windows.Input.Key.Up => "Up",
            System.Windows.Input.Key.Down => "Down",
            _ when key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9 => ((int)key - (int)System.Windows.Input.Key.D0).ToString(),
            _ when key >= System.Windows.Input.Key.NumPad0 && key <= System.Windows.Input.Key.NumPad9 => ((int)key - (int)System.Windows.Input.Key.NumPad0).ToString(),
            _ when key >= System.Windows.Input.Key.A && key <= System.Windows.Input.Key.Z => key.ToString(),
            _ when key >= System.Windows.Input.Key.F1 && key <= System.Windows.Input.Key.F24 => key.ToString(),
            _ => key.ToString(),
        };
    }

    private static string ModifiersToString(ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        return string.Join("+", parts);
    }
}