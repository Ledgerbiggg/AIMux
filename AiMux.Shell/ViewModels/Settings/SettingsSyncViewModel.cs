using System.Windows;
using AiMux.Common.Config;
using AiMux.Common.Logger;
using AiMux.Models;
using AiMux.Services.IService;
using AiMux.Shell.Util;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Mvvm;

namespace AiMux.Shell.ViewModels.Settings;

/// <summary>配置同步面板：通过 WebDAV 服务器统一管理多设备配置 + 本地配置导入导出</summary>
public class SettingsSyncViewModel : BindableBase
{
    private readonly ConfigService _config;
    private readonly AppSettings _settings;

    private string _serverUrl = "";
    public string ServerUrl
    {
        get => _serverUrl;
        set => SetProperty(ref _serverUrl, value);
    }

    private string _username = "";
    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    private string _password = "";
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    private bool _autoSync;
    public bool AutoSync
    {
        get => _autoSync;
        set => SetProperty(ref _autoSync, value);
    }

    /// <summary>是否正在执行操作（禁用按钮/显示进度）</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaisePropertyChanged(nameof(CanOperate));
            }
        }
    }
    private bool _isBusy;

    /// <summary>非忙碌时可操作</summary>
    public bool CanOperate => !IsBusy;

    /// <summary>状态文字</summary>
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }
    private string _status = "";

    /// <summary>上次同步时间显示文本</summary>
    public string LastSyncDisplay
    {
        get => _lastSyncDisplay;
        private set => SetProperty(ref _lastSyncDisplay, value);
    }
    private string _lastSyncDisplay = "从未同步";

    public DelegateCommand SaveCommand { get; }
    public DelegateCommand TestCommand { get; }
    public DelegateCommand UploadCommand { get; }
    public DelegateCommand DownloadCommand { get; }
    public DelegateCommand ExportCommand { get; }
    public DelegateCommand ImportCommand { get; }

    private readonly IWebDavService _webDavService;

    public SettingsSyncViewModel(ConfigService config, IWebDavService webDavService)
    {
        _config = config;
        _settings = config.LoadSettings();
        _webDavService = webDavService;

        ServerUrl = _settings.WebDav.ServerUrl;
        Username = _settings.WebDav.Username;
        Password = _settings.WebDav.Password;
        AutoSync = _settings.WebDav.AutoSync;
        UpdateLastSyncDisplay();

        SaveCommand = new DelegateCommand(Save);
        TestCommand = new DelegateCommand(async () => await TestConnectionAsync());
        UploadCommand = new DelegateCommand(async () => await UploadAsync());
        DownloadCommand = new DelegateCommand(async () => await DownloadAsync());
        ExportCommand = new DelegateCommand(ExportConfig);
        ImportCommand = new DelegateCommand(ImportConfig);
    }

    /// <summary>保存 WebDAV 配置到 settings.json（全覆盖写盘）</summary>
    private void Save()
    {
        _settings.WebDav.ServerUrl = ServerUrl.Trim();
        _settings.WebDav.Username = Username.Trim();
        _settings.WebDav.Password = Password;
        _settings.WebDav.AutoSync = AutoSync;
        _config.SaveSettings(_settings);
        _ = MessageBoxHelper.Info("WebDAV 配置已保存。");
    }

    /// <summary>测试 WebDAV 连接</summary>
    private async Task TestConnectionAsync()
    {
        if (IsBusy) return;

        // 先保存当前输入（不弹窗），让服务能读到最新配置
        SaveSilently();

        IsBusy = true;
        Status = "正在测试连接…";
        try
        {
            var result = await _webDavService.TestConnectionAsync();
            Status = result.Message;
            if (result.Ok)
                _ = MessageBoxHelper.Info(result.Message, "连接测试");
            else
                _ = MessageBoxHelper.Error(result.Message, "连接测试");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("WebDAV 测试连接异常", ex);
            Status = "测试异常：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>上传本地配置到 WebDAV</summary>
    private async Task UploadAsync()
    {
        if (IsBusy) return;

        SaveSilently();

        IsBusy = true;
        Status = "正在上传配置…";
        try
        {
            var result = await _webDavService.UploadAsync();
            Status = result.Message;
            if (result.Ok)
            {
                UpdateLastSyncDisplay();
                _ = MessageBoxHelper.Info(result.Message, "上传配置");
            }
            else
            {
                _ = MessageBoxHelper.Error(result.Message, "上传配置");
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("WebDAV 上传配置异常", ex);
            Status = "上传异常：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>从 WebDAV 拉取远程配置</summary>
    private async Task DownloadAsync()
    {
        if (IsBusy) return;

        var confirm = await MessageBoxHelper.Confirm(
            "从 WebDAV 拉取远程配置将覆盖当前的本地设置和平台列表。\n\n确定要继续吗？",
            "拉取远程配置");
        if (!confirm) return;

        SaveSilently();

        IsBusy = true;
        Status = "正在拉取远程配置…";
        try
        {
            var result = await _webDavService.DownloadAsync();
            Status = result.Message;
            if (result.Ok)
            {
                UpdateLastSyncDisplay();
                _ = MessageBoxHelper.Info(result.Message + "\n\n即将重启以应用全部配置…", "拉取配置");
                RestartApp();
            }
            else
            {
                _ = MessageBoxHelper.Error(result.Message, "拉取配置");
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("WebDAV 下载配置异常", ex);
            Status = "拉取异常：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>静默保存（不弹窗），用于操作前刷新本地配置</summary>
    private void SaveSilently()
    {
        _settings.WebDav.ServerUrl = ServerUrl.Trim();
        _settings.WebDav.Username = Username.Trim();
        _settings.WebDav.Password = Password;
        _settings.WebDav.AutoSync = AutoSync;
        _config.SaveSettings(_settings);
    }

    /// <summary>更新上次同步时间显示（读磁盘最新配置：上传/下载成功后可实时刷新）</summary>
    private void UpdateLastSyncDisplay()
    {
        var raw = _config.LoadSettings().WebDav.LastSyncTime;
        if (string.IsNullOrEmpty(raw))
        {
            LastSyncDisplay = "从未同步";
            return;
        }
        if (DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
        {
            LastSyncDisplay = dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }
        else
        {
            LastSyncDisplay = raw;
        }
    }

    /// <summary>导出配置到 .aimux 文件（设置 + 平台列表）</summary>
    private void ExportConfig()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "AiMux 配置 (*.aimux)|*.aimux|所有文件 (*.*)|*.*",
            FileName = "aimux-config.aimux",
            DefaultExt = ".aimux",
            Title = "导出配置",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _config.ExportConfig(dlg.FileName);
            _ = MessageBoxHelper.Info("配置已导出到：\n" + dlg.FileName);
        }
        catch (Exception ex)
        {
            _ = MessageBoxHelper.Error("导出失败：" + ex.Message);
        }
    }

    /// <summary>从 .aimux 文件导入配置，并重启应用以完全生效</summary>
    private void ImportConfig()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "AiMux 配置 (*.aimux)|*.aimux|所有文件 (*.*)|*.*",
            Title = "导入配置",
        };
        if (dlg.ShowDialog() != true) return;
        var (ok, msg) = _config.ImportConfig(dlg.FileName);
        if (!ok)
        {
            _ = MessageBoxHelper.Error(msg);
            return;
        }
        _ = MessageBoxHelper.Info(msg + "，即将重启以应用全部配置…");
        RestartApp();
    }

    /// <summary>启动新实例并关闭当前进程，确保全部配置重新加载生效</summary>
    private static void RestartApp()
    {
        Task.Delay(700).ContinueWith(_ =>
        {
            var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe))
                System.Diagnostics.Process.Start(exe);
            Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
        });
    }
}
