using System.IO;
using System.Windows;
using System.Windows.Controls;
using AiMux.Common.Config;
using AiMux.Common.Logger;
using AiMux.Models;
using AiMux.Services.IService;
using Microsoft.Web.WebView2.Core;

namespace AiMux.Shell.Controls;

/// <summary>单个平台的 WebView2 宿主：懒加载创建、网页就绪后隐藏占位层、支持自动聚焦
/// 网页加载完成后自动抓取 favicon 并保存，无需手动获取图标</summary>
public partial class WebViewHost : UserControl
{
    private readonly IWebViewService _webViewService;
    private readonly IIconService _iconService;
    private readonly IPlatformService _platformService;
    private bool _initialized;

    /// <summary>当前承载的平台配置</summary>
    public PlatformInfo Platform { get; }

    /// <summary>当前实际网址（导航完成后）变化通知，供主窗口地址栏同步</summary>
    public event Action<string>? AddressChanged;

    /// <summary>摸鱼模式下收到 Esc 的通知：只有主窗口在摸鱼模式时才订阅，
    /// 非摸鱼态不订阅 → 不拦截 Esc，页面仍可用它退出网页全屏</summary>
    public event Action? EscapePressed;

    /// <summary>WebView2 缩放比例下限（官方区间 0.25~5.0）。摸鱼小窗按最小值渲染：
    /// 缩放越小，CSS 视口越宽，桌面版页面铺满小窗、竖版滚动条自然消失</summary>
    private const int MinZoomPercent = 25;

    /// <summary>是否处于摸鱼模式的最小缩放状态</summary>
    private bool _isMiniZoom;

    /// <summary>当前 WebView 实际网址（供地址栏显示 / 复制使用；未初始化时回退到平台配置地址）</summary>
    public string CurrentUrl => WebView.CoreWebView2?.Source?.ToString() ?? Platform.Url;

    public WebViewHost(PlatformInfo platform, IWebViewService webViewService,
        IIconService iconService, IPlatformService platformService)
    {
        Platform = platform;
        _webViewService = webViewService;
        _iconService = iconService;
        _platformService = platformService;
        InitializeComponent();

        // 占位层展示平台首字母与名称
        var initial = platform.Name.Length > 0 ? platform.Name[..1].ToUpperInvariant() : "?";
        BigIconText.Text = initial;
        TitleText.Text = $"{platform.Name} 网页版加载中…";
    }

    /// <summary>懒加载创建 WebView2（首次切换到该平台才调用，实例常驻不销毁）</summary>
    public async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;
        _initialized = true;

        try
        {
            var env = await _webViewService.GetEnvironmentAsync();
            await WebView.EnsureCoreWebView2Async(env);
            // 拦截新窗口请求：不让其弹出外部浏览器 / 独立窗口，统一改为「在当前 WebView 内打开，
            // 旧内容被替换」的通用行为（点开视频 / 链接都留在界面内，不脱离桌面）
            WebView.CoreWebView2.NewWindowRequested += CoreWebView2_OnNewWindowRequested;
            // 订阅页面消息：摸鱼模式下把 Esc 变成"退出小窗"。
            // 注意不能用 CoreWebView2Controller.AcceleratorKeyPressed——WPF 控件不暴露 controller，
            // 只能在页面内挂 keydown 钩子、通过 postMessage 上报（见 EscHookScript）
            WebView.CoreWebView2.WebMessageReceived += CoreWebView2_OnWebMessageReceived;
            // 应用缩放：以设置页配置为准（摸鱼模式则用最小值）。
            // 这里刻意不订阅 ZoomFactorChanged——页面内的临时缩放绝不能反向改写配置
            ApplyConfiguredZoom();
            WebView.Source = new Uri(Platform.Url);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"WebView2 初始化失败: {Platform.Url}", ex);
        }
    }

    /// <summary>页面内 window.open / 新窗口请求：改为在当前 WebView 内导航，旧页面被替换（通用行为）</summary>
    private void CoreWebView2_OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        try
        {
            e.Handled = true; // 标记已处理，阻止 WebView2 把新窗口丢给系统默认浏览器
            if (!string.IsNullOrEmpty(e.Uri))
                WebView.CoreWebView2?.Navigate(e.Uri);
        }
        catch (Exception ex)
        {
            LoggerHelper.Info($"新窗口内打开失败: {e.Uri} - {ex.Message}");
        }
    }

    /// <summary>Esc 钩子脚本：每次导航后重新注入（文档重建后监听器会丢）。
    /// 刻意只在"页面未处于全屏"时上报——网页全屏（如 B 站剧场模式）时 Esc 应先退出全屏，
    /// 第二次 Esc 才退出摸鱼小窗，否则会把网页全屏的退出键抢掉</summary>
    private const string EscHookScript = """
        (function () {
          if (window.__aimuxEscHook) return;
          window.__aimuxEscHook = true;
          document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && !document.fullscreenElement) {
              window.chrome.webview.postMessage('aimux-esc');
            }
          }, true);
        })();
        """;

    /// <summary>接收页面内 Esc 上报：只有主窗口处于摸鱼模式（有人订阅）时才响应。
    /// 非摸鱼模式订阅为空 → 完全不影响页面自己的 Esc 行为</summary>
    private void CoreWebView2_OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var handler = EscapePressed;
        if (handler is null)
            return;
        try
        {
            // 非字符串消息（页面自己的 postMessage）会抛异常，直接忽略
            if (e.TryGetWebMessageAsString() == "aimux-esc")
                handler();
        }
        catch { /* 与本功能无关的页面消息 */ }
    }

    /// <summary>摸鱼模式：切到最小缩放（WebView2 下限 25%）。
    /// 此时 WebView2 可能还没初始化，_isMiniZoom 先记下意图，初始化时 ApplyConfiguredZoom 会补上</summary>
    public void ApplyMiniZoom()
    {
        _isMiniZoom = true;
        ApplyConfiguredZoom();
    }

    /// <summary>退出摸鱼模式：还原为设置页配置的缩放比例</summary>
    public void RestoreZoom()
    {
        _isMiniZoom = false;
        ApplyConfiguredZoom();
    }

    /// <summary>应用当前应生效的缩放比例：摸鱼模式用最小值，否则用设置页里的平台配置值。
    /// 这是缩放的唯一入口——初始化、每次导航完成、每次切回该平台都会重新应用，
    /// 所以在页面里按 Ctrl+滚轮 的临时缩放会被配置值覆盖，且永远不会被写回配置</summary>
    public void ApplyConfiguredZoom()
    {
        try
        {
            if (WebView.CoreWebView2 is null)
                return;
            var percent = _isMiniZoom ? MinZoomPercent : NormalizeZoom(Platform.ZoomPercent);
            var factor = percent / 100.0;
            if (Math.Abs(WebView.ZoomFactor - factor) > 0.001)
                WebView.ZoomFactor = factor;
        }
        catch (Exception ex)
        {
            LoggerHelper.Info($"应用缩放比例失败: {ex.Message}");
        }
    }

    /// <summary>网页后退（浏览器历史记录）；无可后退项时静默忽略</summary>
    public void GoBack() => GoHistory(back: true);

    /// <summary>网页前进（浏览器历史记录）；无可前进项时静默忽略</summary>
    public void GoForward() => GoHistory(back: false);

    private void GoHistory(bool back)
    {
        try
        {
            var core = WebView.CoreWebView2;
            if (core is null)
                return;
            if (back && core.CanGoBack)
                core.GoBack();
            else if (!back && core.CanGoForward)
                core.GoForward();
        }
        catch (Exception ex)
        {
            LoggerHelper.Info($"网页{(back ? "后退" : "前进")}失败: {ex.Message}");
        }
    }

    /// <summary>外部（地址栏）调用：在当前 WebView 内跳转到指定网址，新内容替换旧内容（通用）</summary>
    public void Navigate(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            if (WebView.CoreWebView2 != null)
                WebView.CoreWebView2.Navigate(url);
            else
                WebView.Source = new Uri(url);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"导航失败: {url}", ex);
        }
    }

    /// <summary>网页加载完成后隐藏占位层，并自动抓取 favicon 保存
    /// 无图标时静默抓取一次，避免用户每次都手动获取</summary>
    private async void WebView_OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            Placeholder.Visibility = Visibility.Collapsed;
            // 每次导航完成都重新压一遍配置里的缩放：设置页的值是强制值，
            // 页面内的临时缩放（Ctrl+滚轮）不能"跟着页面走"
            ApplyConfiguredZoom();
            // 导航后文档重建，Esc 钩子必须重新注入（非摸鱼模式下上报会被忽略，无副作用）
            try { await WebView.CoreWebView2.ExecuteScriptAsync(EscHookScript); }
            catch (Exception ex) { LoggerHelper.Info($"注入 Esc 钩子失败: {ex.Message}"); }
        }

        // 通知外部（地址栏）当前实际网址，便于同步显示
        try { AddressChanged?.Invoke(WebView.Source?.ToString() ?? ""); }
        catch { }

        // 已有本地图标缓存则不再重复抓取，避免每次导航都写文件
        if (!string.IsNullOrEmpty(Platform.Icon) && File.Exists(Platform.Icon))
            return;

        // 导航成功才尝试抓取 favicon，失败静默忽略
        if (!e.IsSuccess)
            return;

        try
        {
            var path = await _iconService.AutoFetchAsync(Platform);
            if (string.IsNullOrEmpty(path))
                return;

            Platform.Icon = path;
            // 通知主界面刷新：保存触发 PlatformsChanged 事件，侧边栏图标自动更新
            var all = _platformService.GetAll();
            var target = all.FirstOrDefault(p => p.Id == Platform.Id);
            if (target is not null)
            {
                target.Icon = path;
                _platformService.Save(all);
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Info($"自动抓取 favicon 失败 {Platform.Url}: {ex.Message}");
        }
    }

    /// <summary>自动聚焦当前平台的输入框（窗口呼出后调用，失败不阻塞）</summary>
    public async Task<bool> FocusInputAsync() =>
        await _webViewService.FocusInputAsync(WebView.CoreWebView2, Platform.FocusSelector);

    /// <summary>刷新当前平台网页</summary>
    public void Reload() => WebView.Reload();

    /// <summary>把缩放百分数规范到合法区间（25 ~ 500），非法回退 100</summary>
    private static int NormalizeZoom(int percent)
        => percent is >= 25 and <= 500 ? percent : 100;
}
