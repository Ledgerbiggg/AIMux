using System.Diagnostics;
using System.Threading.Tasks;
using AiMux.Services.IService;
using AiMux.Services.Service;
using AiMux.Shell.Util;
using AiMux.Shell.ViewModels;
using Prism.Commands;
using Prism.Mvvm;

namespace AiMux.Shell.ViewModels.Settings;

/// <summary>设置-关于：显示版本、仓库链接，并提供「检查更新」</summary>
public class SettingsAboutViewModel : BindableBase
{
    /// <summary>本地程序版本号（与界面统一，原样三段，如 0.6.0）</summary>
    public string Version { get; set; } = "0.0.0";

    /// <summary>项目仓库地址</summary>
    public string GitHubUrl { get; } = "https://github.com/Ledgerbiggg/AIMux";

    /// <summary>上次检查的状态文字</summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    private string _status = "尚未检查更新";

    /// <summary>是否正在检查（用于禁用按钮/显示进度）</summary>
    public bool IsChecking { get => _isChecking; private set => SetProperty(ref _isChecking, value); }
    private bool _isChecking;

    public DelegateCommand CheckCommand { get; }
    public DelegateCommand OpenGitHubCommand { get; }

    private readonly IUpdateService _update = new UpdateService();

    public SettingsAboutViewModel()
    {
        Version = MainViewModel.GetVersionString();
        CheckCommand = new DelegateCommand(async () => await CheckAsync());
        OpenGitHubCommand = new DelegateCommand(OpenGitHub);
    }

    private void OpenGitHub() =>
        Process.Start(new ProcessStartInfo(GitHubUrl) { UseShellExecute = true });

    private async Task CheckAsync()
    {
        if (IsChecking)
            return;

        IsChecking = true;
        Status = "正在检查更新…";
        try
        {
            var info = await _update.FetchLatestAsync();
            if (info == null)
            {
                Status = "检查失败：无法访问更新源（请检查网络）";
                return;
            }

            if (UpdateService.IsNewer(info.Version, Version))
            {
                Status = $"发现新版本 {info.Version}（当前 {Version}）";
                var msg = $"发现新版本 {info.Version}（当前 {Version}）";
                if (!string.IsNullOrWhiteSpace(info.Notes))
                    msg += $"\n\n更新内容：\n{info.Notes}";
                msg += "\n\n是否打开下载页面？";

                var go = await MessageBoxHelper.Confirm(msg, "检查更新");
                if (go && !string.IsNullOrWhiteSpace(info.Url))
                    Process.Start(new ProcessStartInfo(info.Url) { UseShellExecute = true });
            }
            else
            {
                Status = $"已是最新版本（{Version}）";
                _ = MessageBoxHelper.Info($"当前已是最新版本（{Version}）", "检查更新");
            }
        }
        finally
        {
            IsChecking = false;
        }
    }
}
