using System.Threading.Tasks;
using System.Windows;
// 别名消歧：Wpf.Ui.Controls 与 System.Windows 都有 MessageBox / MessageBoxResult
using WpfMessageBox = Wpf.Ui.Controls.MessageBox;
using WpfMessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace AiMux.Shell.Util;

/// <summary>基于 WPF-UI 的 MessageBox 封装：在激活的 FluentWindow 上弹出美观对话框（替代系统默认 MessageBox）</summary>
public static class MessageBoxHelper
{
    public static Task Info(string message, string title = "AiMux") => Show(title, message);

    public static Task Warn(string message, string title = "AiMux") => Show(title, message);

    public static Task Error(string message, string title = "AiMux") => Show(title, message);

    /// <summary>确认对话框：返回 true 表示用户点击了「确认」（Primary）按钮</summary>
    public static async Task<bool> Confirm(string message, string title = "AiMux")
    {
        var msg = new WpfMessageBox
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "确认",
            CloseButtonText = "取消",
        };
        AttachOwner(msg);
        var result = await msg.ShowDialogAsync();
        return result == WpfMessageBoxResult.Primary;
    }

    private static Task Show(string title, string message)
    {
        var msg = new WpfMessageBox
        {
            Title = title,
            Content = message,
            CloseButtonText = "确定",
        };
        AttachOwner(msg);
        return msg.ShowDialogAsync();
    }

    /// <summary>给对话框挂上"当前活动且可见"的 Owner。
    /// 无 Owner 的模态框会被 WPF 当作独立顶层窗口参与"最后一个窗口关闭"的判定，
    /// 而本应用的主窗口会隐藏到托盘——不挂 Owner 就容易连累整个窗口生命周期，必须显式指定。
    /// 若当前没有任何可见窗口（例如从托盘菜单触发），保持无 Owner 由系统居中到屏幕</summary>
    private static void AttachOwner(Window dialog)
    {
        try
        {
            var current = Application.Current;
            if (current is null)
                return;

            var owner = current.Windows.OfType<Window>()
                .FirstOrDefault(w => w.IsActive && w.IsVisible);

            // 没有活动窗口时退而求其次用可见的主窗口；主窗口已隐藏则保持无 Owner
            if (owner is null && current.MainWindow is { IsVisible: true } main)
                owner = main;

            if (owner is not null && !ReferenceEquals(owner, dialog))
                dialog.Owner = owner;
        }
        catch
        {
            // 设置 Owner 失败不影响弹窗本身显示
        }
    }
}
