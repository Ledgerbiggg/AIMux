using Microsoft.Web.WebView2.Core;

namespace AiMux.Services.IService;

/// <summary>WebView2 基础设施服务：共享环境、运行时检测、自动聚焦脚本</summary>
public interface IWebViewService
{
    /// <summary>检测本机是否安装 WebView2 Runtime，缺失返回 false</summary>
    Task<bool> EnsureRuntimeAsync();

    /// <summary>获取共享 CoreWebView2Environment（固定 UserDataFolder，登录态持久化）</summary>
    Task<CoreWebView2Environment> GetEnvironmentAsync();

    /// <summary>构建自动聚焦输入框的 JS 脚本（指定选择器 + 通用兜底）</summary>
    string BuildFocusScript(string focusSelector);

    /// <summary>对指定 WebView2 执行聚焦脚本，返回是否聚焦成功</summary>
    Task<bool> FocusInputAsync(CoreWebView2 coreWebView2, string focusSelector);
}
