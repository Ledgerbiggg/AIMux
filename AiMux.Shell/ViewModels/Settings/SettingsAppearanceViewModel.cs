using System.Windows;
using AiMux.Common.Config;
using AiMux.Models;
using Prism.Commands;
using Prism.Mvvm;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AiMux.Shell.ViewModels.Settings;

/// <summary>外观面板：浅色/深色主题切换
/// 主题切换完全交给 WPF-UI 的 ApplicationThemeManager，所有控件引用 WPF-UI 的
/// DynamicResource（ApplicationBackgroundBrush 等），切换时自动跟随，无需手动改 brush</summary>
public class SettingsAppearanceViewModel : BindableBase
{
    private readonly ConfigService _config;
    private readonly AppSettings _settings;

    private bool _isLight;
    public bool IsLight
    {
        get => _isLight;
        set => SetProperty(ref _isLight, value);
    }

    private bool _isDark;
    public bool IsDark
    {
        get => _isDark;
        set => SetProperty(ref _isDark, value);
    }

    // ===== 主界面按钮显隐（导航条 7 个按钮，取消勾选即隐藏）=====
    private bool _showCopyUrl;
    public bool ShowCopyUrl { get => _showCopyUrl; set => SetProperty(ref _showCopyUrl, value); }

    private bool _showHome;
    public bool ShowHome { get => _showHome; set => SetProperty(ref _showHome, value); }

    private bool _showCompactToggle;
    public bool ShowCompactToggle { get => _showCompactToggle; set => SetProperty(ref _showCompactToggle, value); }

    private bool _showReload;
    public bool ShowReload { get => _showReload; set => SetProperty(ref _showReload, value); }

    private bool _showThemeToggle;
    public bool ShowThemeToggle { get => _showThemeToggle; set => SetProperty(ref _showThemeToggle, value); }

    private bool _showPinToggle;
    public bool ShowPinToggle { get => _showPinToggle; set => SetProperty(ref _showPinToggle, value); }

    private bool _showMiniMode;
    public bool ShowMiniMode { get => _showMiniMode; set => SetProperty(ref _showMiniMode, value); }

    public DelegateCommand SaveCommand { get; }

    public SettingsAppearanceViewModel(ConfigService config)
    {
        _config = config;
        _settings = config.LoadSettings();
        _isLight = _settings.Theme != "Dark";
        _isDark = !_isLight;
        var ui = _settings.Ui;
        _showCopyUrl = ui.ShowCopyUrl;
        _showHome = ui.ShowHome;
        _showCompactToggle = ui.ShowCompactToggle;
        _showReload = ui.ShowReload;
        _showThemeToggle = ui.ShowThemeToggle;
        _showPinToggle = ui.ShowPinToggle;
        _showMiniMode = ui.ShowMiniMode;
        SaveCommand = new DelegateCommand(Save);
    }

    /// <summary>应用并保存主题与按钮显隐</summary>
    private void Save()
    {
        var theme = IsDark ? "Dark" : "Light";
        _settings.Theme = theme;
        var ui = _settings.Ui;
        ui.ShowCopyUrl = ShowCopyUrl;
        ui.ShowHome = ShowHome;
        ui.ShowCompactToggle = ShowCompactToggle;
        ui.ShowReload = ShowReload;
        ui.ShowThemeToggle = ShowThemeToggle;
        ui.ShowPinToggle = ShowPinToggle;
        ui.ShowMiniMode = ShowMiniMode;
        _config.SaveSettings(_settings);
        ApplyTheme(theme);
    }

    /// <summary>应用 WPF-UI 主题：所有引用 WPF-UI DynamicResource 的控件自动跟随
    /// 应用启动与设置保存共用此入口</summary>
    public static void ApplyTheme(string theme)
    {
        var isDark = theme == "Dark";
        // WPF-UI 自带主题切换：所有用 DynamicResource 引用 WPF-UI 资源的控件自动变色
        ApplicationThemeManager.Apply(
            isDark ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.Mica, true);
    }
}
