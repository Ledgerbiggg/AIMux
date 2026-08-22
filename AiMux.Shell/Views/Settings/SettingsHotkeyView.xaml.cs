using System.Windows.Input;
using AiMux.Shell.ViewModels.Settings;

namespace AiMux.Shell.Views.Settings;

/// <summary>热键设置视图：录制态下拦截 PreviewKeyDown 分发给对应行</summary>
public partial class SettingsHotkeyView
{
    public SettingsHotkeyView()
    {
        InitializeComponent();
    }

    /// <summary>录制态下拦截按键：分发到 IsRecording=true 的那一行
    /// 任何时候只允许一行录制，其他行的录制状态会自动取消</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsHotkeyViewModel vm) return;

        var recordingRow = vm.Rows.FirstOrDefault(r => r.IsRecording);
        if (recordingRow is null) return;

        // 拦截所有按键
        e.Handled = true;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        recordingRow.CaptureKey(Keyboard.Modifiers, key);
    }
}