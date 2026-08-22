using System.Drawing;
using System.IO;
using System.Windows.Forms;
using AiMux.Services.IService;

namespace AiMux.Services.Service;

/// <summary>系统托盘服务实现（基于 System.Windows.Forms.NotifyIcon）：
/// 左键单击图标打开/呼出主界面，右键菜单显示/隐藏与退出</summary>
public class TrayService : ITrayService
{
    private NotifyIcon? _trayIcon;
    private bool _disposed;

    /// <summary>托盘"显示/隐藏"请求（右键菜单触发，行为为切换）</summary>
    public event EventHandler? ShowRequested;

    /// <summary>托盘"打开/呼出"请求（左键单击图标触发，行为仅为显示，不与隐藏串）</summary>
    public event EventHandler? OpenRequested;

    /// <summary>托盘"退出"请求（菜单触发）</summary>
    public event EventHandler? ExitRequested;

    /// <summary>创建并显示托盘图标（使用 Assets 中的 .ico）</summary>
    public void Show()
    {
        if (_trayIcon is not null)
            return;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon_ico_128x128.ico");
        Icon icon;
        if (File.Exists(iconPath))
            icon = new Icon(iconPath);
        else
            icon = SystemIcons.Application; // 兜底

        _trayIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "AI Chat Hub",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        // 左键单击图标：仅打开/呼出（不隐藏，避免与快捷键隐藏逻辑串）
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                OpenRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>隐藏并销毁托盘图标</summary>
    public void Hide()
    {
        if (_trayIcon is null)
            return;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    /// <summary>构建托盘右键菜单（ContextMenuStrip）</summary>
    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var toggle = new ToolStripMenuItem("显示 / 隐藏");
        toggle.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(toggle);
        menu.Items.Add(exit);
        return menu;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Hide();
    }
}
