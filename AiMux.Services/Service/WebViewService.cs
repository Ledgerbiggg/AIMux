using AiMux.Common.Config;
using AiMux.Common.Logger;
using AiMux.Services.IService;
using Microsoft.Web.WebView2.Core;

namespace AiMux.Services.Service;

/// <summary>WebView2 基础设施服务：共享环境（固定 UserDataFolder 持久化登录态）、运行时检测、聚焦脚本</summary>
public class WebViewService : IWebViewService
{
    private readonly ConfigService _config;
    private readonly SemaphoreSlim _envLock = new(1, 1);
    private CoreWebView2Environment? _environment;

    public WebViewService(ConfigService config) => _config = config;

    /// <summary>检测本机是否安装 WebView2 Runtime（Evergreen 版本字符串）</summary>
    public Task<bool> EnsureRuntimeAsync()
    {
        try
        {
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("未检测到 WebView2 Runtime", ex);
            return Task.FromResult(false);
        }
    }

    /// <summary>获取共享环境：所有 WebView2 实例复用同一 UserDataFolder，登录态按域名自动隔离</summary>
    public async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (_environment is not null)
            return _environment;

        await _envLock.WaitAsync();
        try
        {
            _environment ??= await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: _config.WebViewDataDir);
            return _environment;
        }
        finally
        {
            _envLock.Release();
        }
    }

    /// <summary>构建聚焦脚本：先试指定选择器，再按优先级兜底常见输入框</summary>
    public string BuildFocusScript(string focusSelector)
    {
        var selectors = new List<string>();
        if (!string.IsNullOrWhiteSpace(focusSelector))
            selectors.Add(focusSelector);
        selectors.AddRange(["textarea", "div[contenteditable=\"true\"]", "[role=\"textbox\"]"]);

        var arr = "[" + string.Join(",", selectors.Select(s => $"\"{s.Replace("\"", "\\\"")}\"")) + "]";
        return $$"""
            (function () {
              var sels = {{arr}};
              for (var i = 0; i < sels.length; i++) {
                try {
                  var el = document.querySelector(sels[i]);
                  if (el) { el.focus(); return true; }
                } catch (e) {}
              }
              return false;
            })();
            """;
    }

    /// <summary>对指定 WebView2 执行聚焦脚本，返回是否聚焦成功（失败不阻塞）</summary>
    public async Task<bool> FocusInputAsync(CoreWebView2 coreWebView2, string focusSelector)
    {
        try
        {
            if (coreWebView2 is null)
                return false;
            var result = await coreWebView2.ExecuteScriptAsync(BuildFocusScript(focusSelector));
            return result.Trim() == "true";
        }
        catch
        {
            return false;
        }
    }
}
