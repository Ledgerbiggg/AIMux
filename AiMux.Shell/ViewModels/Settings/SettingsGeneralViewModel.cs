using AiMux.Common.Config;
using AiMux.Common.Logger;
using AiMux.Models;
using AiMux.Services.IService;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Mvvm;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using AiMux.Shell.Util;
using AiMux.Shell.ViewModels;

namespace AiMux.Shell.ViewModels.Settings;

/// <summary>通用设置面板：默认平台、启动/失焦行为、窗口位置记忆</summary>
public class SettingsGeneralViewModel : BindableBase
{
    private readonly ConfigService _config;
    private readonly AppSettings _settings;

    /// <summary>默认平台下拉数据源</summary>
    public List<PlatformInfo> Platforms { get; }

    private string _defaultPlatformId = "";
    public string DefaultPlatformId
    {
        get => _defaultPlatformId;
        set => SetProperty(ref _defaultPlatformId, value);
    }

    private bool _startHidden;
    public bool StartHidden
    {
        get => _startHidden;
        set => SetProperty(ref _startHidden, value);
    }

    /// <summary>是否开机自动启动（写入注册表 Run 项）</summary>
    private bool _autoStart;
    public bool AutoStart
    {
        get => _autoStart;
        set => SetProperty(ref _autoStart, value);
    }

    private bool _rememberPosition;
    public bool RememberPosition
    {
        get => _rememberPosition;
        set => SetProperty(ref _rememberPosition, value);
    }

    // —— 小窗 / 大窗尺寸（滑块可调，带范围限制，防止过小导致界面不可用）——
    public const double CompactWidthMin = 360, CompactWidthMax = 900;
    public const double CompactHeightMin = 480, CompactHeightMax = 1100;
    public const double FullWidthMin = 800, FullWidthMax = 2560;
    public const double FullHeightMin = 600, FullHeightMax = 1600;

    private double _compactWidth;
    public double CompactWidth
    {
        get => _compactWidth;
        set => SetProperty(ref _compactWidth, Clamp(value, CompactWidthMin, CompactWidthMax));
    }

    private double _compactHeight;
    public double CompactHeight
    {
        get => _compactHeight;
        set => SetProperty(ref _compactHeight, Clamp(value, CompactHeightMin, CompactHeightMax));
    }

    private double _fullWidth;
    public double FullWidth
    {
        get => _fullWidth;
        set => SetProperty(ref _fullWidth, Clamp(value, FullWidthMin, FullWidthMax));
    }

    private double _fullHeight;
    public double FullHeight
    {
        get => _fullHeight;
        set => SetProperty(ref _fullHeight, Clamp(value, FullHeightMin, FullHeightMax));
    }

    private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

    /// <summary>当前程序版本号（取自程序集版本，供关于/更新使用）</summary>
    public string Version { get; set; } = "0.0.0";

    public DelegateCommand SaveCommand { get; }

    /// <summary>恢复默认配置：清空所有用户数据并重启</summary>
    public DelegateCommand ResetCommand { get; }

    public SettingsGeneralViewModel(ConfigService config, IPlatformService platformService)
    {
        _config = config;
        _settings = config.LoadSettings();
        Platforms = platformService.GetAll();

        DefaultPlatformId = _settings.Behavior.DefaultPlatformId;
        StartHidden = _settings.Behavior.StartHidden;
        AutoStart = GetAutoStart();
        RememberPosition = _settings.Window.RememberPosition;
        CompactWidth = _settings.Window.CompactWidth;
        CompactHeight = _settings.Window.CompactHeight;
        FullWidth = _settings.Window.FullWidth;
        FullHeight = _settings.Window.FullHeight;
        SaveCommand = new DelegateCommand(Save);
        ExportCommand = new DelegateCommand(ExportConfig);
        ImportCommand = new DelegateCommand(ImportConfig);
        ResetCommand = new DelegateCommand(ResetToDefault);
        Version = MainViewModel.GetVersionString();
    }

    /// <summary>保存通用设置到 settings.json</summary>
    private void Save()
    {
        _settings.Behavior.DefaultPlatformId = DefaultPlatformId;
        _settings.Behavior.StartHidden = StartHidden;
        SetAutoStart(AutoStart);
        _settings.Window.RememberPosition = RememberPosition;
        _settings.Window.CompactWidth = Clamp(CompactWidth, CompactWidthMin, CompactWidthMax);
        _settings.Window.CompactHeight = Clamp(CompactHeight, CompactHeightMin, CompactHeightMax);
        _settings.Window.FullWidth = Clamp(FullWidth, FullWidthMin, FullWidthMax);
        _settings.Window.FullHeight = Clamp(FullHeight, FullHeightMin, FullHeightMax);
        _config.SaveSettings(_settings);
        _ = MessageBoxHelper.Info("通用设置已保存。");
    }

    /// <summary>读取开机自启状态（注册表 Run 项是否存在 AiMux）</summary>
    private static bool GetAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("AiMux") is string v && !string.IsNullOrEmpty(v);
    }

    /// <summary>开启/关闭开机自启：写入或删除注册表 Run 项</summary>
    private static void SetAutoStart(bool enable)
    {
        var exePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? "";
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enable)
        {
            if (!string.IsNullOrEmpty(exePath))
            {
                key.SetValue("AiMux", exePath);
                LoggerHelper.Info($"已写入开机自启: {exePath}");
            }
        }
        else
        {
            key.DeleteValue("AiMux", false);
            LoggerHelper.Info("已移除开机自启");
        }
    }

    /// <summary>导出配置到 .aimux 文件（设置 + 平台列表）</summary>
    public DelegateCommand ExportCommand { get; }

    /// <summary>从 .aimux 文件导入配置，并重启应用以完全生效</summary>
    public DelegateCommand ImportCommand { get; }

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
        // 启动新实例后关闭当前，确保窗口/热键/平台/主题全部重新加载生效
        RestartApp();
    }

    /// <summary>恢复默认配置：二次确认后清空所有用户数据并重启应用</summary>
    private async void ResetToDefault()
    {
        var confirm = await MessageBoxHelper.Confirm(
            "确定将全部配置恢复到初始状态吗？\n\n将清空所有 AI 平台、窗口尺寸、热键、外观等设置，此操作不可撤销。",
            "恢复默认配置");
        if (!confirm)
            return;

        _config.ResetToDefault();
        _ = MessageBoxHelper.Info("已恢复默认配置，即将重启…");
        RestartApp();
    }

    /// <summary>启动新实例并关闭当前进程，确保全部配置重新加载生效</summary>
    private static void RestartApp()
    {
        Task.Delay(700).ContinueWith(_ =>
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe))
                Process.Start(exe);
            Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
        });
    }
}
