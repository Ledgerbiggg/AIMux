using AiMux.Common.Config;
using AiMux.Common.Hotkey;
using AiMux.Services.IService;
using AiMux.Shell.ViewModels.Settings;
using Prism.Mvvm;

namespace AiMux.Shell.ViewModels;

/// <summary>设置窗口 ViewModel：左侧导航切换四个设置面板</summary>
public class SettingsViewModel : BindableBase
{
    /// <summary>左侧导航项</summary>
    public List<string> NavItems { get; } = ["平台管理", "通用设置", "热键设置", "外观", "关于"];

    /// <summary>平台管理面板 VM</summary>
    public SettingsPlatformViewModel PlatformVm { get; }

    /// <summary>通用设置面板 VM</summary>
    public SettingsGeneralViewModel GeneralVm { get; }

    /// <summary>热键设置面板 VM</summary>
    public SettingsHotkeyViewModel HotkeyVm { get; }

    /// <summary>外观面板 VM</summary>
    public SettingsAppearanceViewModel AppearanceVm { get; }

    /// <summary>关于面板 VM</summary>
    public SettingsAboutViewModel AboutVm { get; }

    private string _selectedNav;
    public string SelectedNav
    {
        get => _selectedNav;
        set
        {
            SetProperty(ref _selectedNav, value);
            UpdatePanel();
        }
    }

    private object _currentPanel;
    public object CurrentPanel
    {
        get => _currentPanel;
        private set => SetProperty(ref _currentPanel, value);
    }

    public SettingsViewModel(IPlatformService platformService, IIconService iconService,
        ConfigService config, HotkeyManager hotkeyManager)
    {
        PlatformVm = new SettingsPlatformViewModel(platformService, iconService);
        GeneralVm = new SettingsGeneralViewModel(config, platformService);
        HotkeyVm = new SettingsHotkeyViewModel(config, hotkeyManager);
        AppearanceVm = new SettingsAppearanceViewModel(config);
        AboutVm = new SettingsAboutViewModel();

        _selectedNav = NavItems[0];
        _currentPanel = PlatformVm;
    }

    /// <summary>根据选中导航项切换面板</summary>
    private void UpdatePanel() =>
        CurrentPanel = SelectedNav switch
        {
            "通用设置" => GeneralVm,
            "热键设置" => HotkeyVm,
            "外观" => AppearanceVm,
            "关于" => AboutVm,
            _ => PlatformVm,
        };
}
