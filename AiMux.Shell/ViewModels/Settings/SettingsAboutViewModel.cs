using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using AiMux.Common.Logger;
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
    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetProperty(ref _isChecking, value)) RaisePropertyChanged(nameof(IsBusy));
        }
    }
    private bool _isChecking;

    /// <summary>是否正在下载安装包</summary>
    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (SetProperty(ref _isDownloading, value)) RaisePropertyChanged(nameof(IsBusy));
        }
    }
    private bool _isDownloading;

    /// <summary>下载进度 0-100（下载中展示百分比）</summary>
    public int DownloadProgress { get => _downloadProgress; private set => SetProperty(ref _downloadProgress, value); }
    private int _downloadProgress;

    /// <summary>检查或下载进行中（界面按钮禁用/状态常显）</summary>
    public bool IsBusy => IsChecking || IsDownloading;

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
        if (IsBusy)
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
                var msg = $"发现新版本 {info.Version}（当前 {Version}）";
                if (!string.IsNullOrWhiteSpace(info.Notes))
                    msg += $"\n\n更新内容：\n{info.Notes}";
                msg += "\n\n是否立即下载并安装？";

                if (await MessageBoxHelper.Confirm(msg, "发现新版本"))
                    await DownloadAndRunInstallerAsync(info);
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

    /// <summary>下载安装包 → 确认后就地启动安装向导 → 退出主程序释放文件占用，完成升级</summary>
    private async Task DownloadAndRunInstallerAsync(UpdateInfo info)
    {
        IsDownloading = true;
        DownloadProgress = 0;
        Status = $"正在下载安装包 {info.Version}… 0%";
        try
        {
            var progress = new Progress<int>(p =>
            {
                DownloadProgress = p;
                Status = $"正在下载安装包 {info.Version}… {p}%";
            });
            var installer = await _update.DownloadInstallerAsync(info.Version, progress);
            if (installer == null)
            {
                Status = "下载失败：请检查网络后重试，或手动前往 GitHub 下载";
                var manual = await MessageBoxHelper.Confirm(
                    "安装包下载失败，是否打开下载页面手动安装？", "更新失败");
                if (manual && !string.IsNullOrWhiteSpace(info.Url))
                    Process.Start(new ProcessStartInfo(info.Url) { UseShellExecute = true });
                return;
            }

            Status = "下载完成，正在启动安装程序…";
            if (!await MessageBoxHelper.Confirm(
                    $"安装包已下载完成（{info.Version}）。\n\n点击「确认」将启动安装向导，请按提示完成安装；安装完成后即可使用新版本。",
                    "更新就绪"))
            {
                Status = $"已取消安装（安装包保留在 {installer}）";
                return;
            }

            // 启动安装向导（UAC 提权由系统接管），随后退出主程序避免 exe 文件被占用
            Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true });
            await Task.Delay(1000);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("自动更新流程异常", ex);
            Status = "更新流程异常，请手动下载安装";
        }
        finally
        {
            IsDownloading = false;
        }
    }
}
