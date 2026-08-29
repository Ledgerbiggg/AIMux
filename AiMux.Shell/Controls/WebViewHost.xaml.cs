using System.IO;
using System.Windows;
using System.Windows.Controls;
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
            Placeholder.Visibility = Visibility.Collapsed;

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
}
