namespace AiMux.Services.IService;

/// <summary>系统托盘服务：托盘图标常驻与菜单交互</summary>
public interface ITrayService : IDisposable
{
    /// <summary>托盘"显示/隐藏"请求（右键菜单触发，行为为切换）</summary>
    event EventHandler? ShowRequested;

    /// <summary>托盘"打开/呼出"请求（双击图标触发，行为仅为显示，不与隐藏串）</summary>
    event EventHandler? OpenRequested;

    /// <summary>托盘"退出"请求（菜单触发）</summary>
    event EventHandler? ExitRequested;

    /// <summary>显示托盘图标</summary>
    void Show();

    /// <summary>隐藏托盘图标</summary>
    void Hide();
}
