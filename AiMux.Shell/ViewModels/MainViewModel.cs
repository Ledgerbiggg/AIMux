using System.Collections.ObjectModel;
using System.Windows;
using AiMux.Common.Config;
using AiMux.Common.Logger;
using AiMux.Services.IService;
using AiMux.Shell.Util;
using AiMux.Shell.Views;
using Prism.Commands;
using Prism.Ioc;
using Prism.Mvvm;

namespace AiMux.Shell.ViewModels;

/// <summary>主窗口 ViewModel：平台列表、选中项、侧边栏折叠状态与全局命令</summary>
public class MainViewModel : BindableBase
{
    private readonly IPlatformService _platformService;
    private readonly IIconService _iconService;
    private readonly ConfigService _config;

    /// <summary>侧边栏平台列表</summary>
    public ObservableCollection<PlatformItem> Platforms { get; } = [];

    /// <summary>当前选中平台（切换时主窗口据此切换 WebView 实例）</summary>
    private PlatformItem? _selectedPlatform;
    public PlatformItem? SelectedPlatform
    {
        get => _selectedPlatform;
        set => SetProperty(ref _selectedPlatform, value);
    }

    /// <summary>侧边栏是否折叠为纯图标态（折叠=60 宽只显示图标，展开=224 宽显示图标+平台名）</summary>
    private bool _isSidebarCollapsed;
    public bool IsSidebarCollapsed
    {
        get => _isSidebarCollapsed;
        set => SetProperty(ref _isSidebarCollapsed, value);
    }

    public string Title => "AI Chat Hub";

    /// <summary>主界面左上角展示的版本号：原样三段显示，如 0.5.1。</summary>
    public string Version => GetVersionString();

    /// <summary>
    /// 统一读取版本号（三段，原样显示，如 0.5.1）。
    /// 读取顺序（均为安装安全路径，不依赖开发期目录结构）：
    ///   1) 程序输出目录下的 version.json（发布时已复制进来，安装后唯一可靠来源）；
    ///   2) 程序集版本 Assembly.GetName().Version，强制取前三段（避免 .NET 默认四段 0.5.1.0）。
    /// 任何一步失败都回退 "0.0.0"，绝不抛异常导致调用方（如设置窗口）打不开。
    /// </summary>
    internal static string GetVersionString()
    {
        try
        {
            // 1) 安装后/发布后：BaseDirectory 同目录的 version.json
            var vj = System.IO.Path.Combine(AppContext.BaseDirectory, "version.json");
            if (System.IO.File.Exists(vj))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(vj));
                if (doc.RootElement.TryGetProperty("version", out var ve))
                {
                    var s = ve.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(s)) return Normalize(s);
                }
            }
        }
        catch { /* 忽略，走回退 */ }

        try
        {
            // 2) 回退：程序集版本，强制三段
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (v is not null) return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch { }

        return "0.0.0";
    }

    /// <summary>把任意版本串规范成 "主.次.修" 三段（多余段截断，缺失段补 0）</summary>
    private static string Normalize(string raw)
    {
        var parts = raw.Split('.');
        var maj = parts.Length > 0 && int.TryParse(parts[0], out var a) ? a : 0;
        var min = parts.Length > 1 && int.TryParse(parts[1], out var b) ? b : 0;
        var bld = parts.Length > 2 && int.TryParse(parts[2], out var c) ? c : 0;
        return $"{maj}.{min}.{bld}";
    }

    /// <summary>浏览器导航条显示的当前平台地址</summary>
    public string CurrentUrl => SelectedPlatform?.Info.Url ?? "";

    /// <summary>浏览器导航条显示的当前平台名称</summary>
    public string CurrentName => SelectedPlatform?.Name ?? "";

    /// <summary>底部提示条文案</summary>
    public string HintText => "WebView2 内嵌真实网页 · 平台切换实例常驻不销毁 · 小窗呼出自动聚焦输入框 · Cookie 持久化";

    /// <summary>折叠/展开侧边栏命令</summary>
    public DelegateCommand ToggleSidebarCommand { get; }

    /// <summary>打开设置窗口命令</summary>
    public DelegateCommand OpenSettingsCommand { get; }

    /// <summary>刷新当前网页命令（主窗口监听事件执行）</summary>
    public DelegateCommand ReloadCommand { get; }

    /// <summary>请求刷新当前平台网页</summary>
    public event EventHandler? ReloadRequested;

    public MainViewModel(IPlatformService platformService, IIconService iconService, ConfigService config)
    {
        _platformService = platformService;
        _iconService = iconService;
        _config = config;

        ToggleSidebarCommand = new DelegateCommand(ToggleSidebar);
        OpenSettingsCommand = new DelegateCommand(OpenSettings);
        ReloadCommand = new DelegateCommand(() => ReloadRequested?.Invoke(this, EventArgs.Empty));

        _platformService.PlatformsChanged += (_, _) => RefreshPlatforms();
        LoadPlatforms();
    }

    /// <summary>加载平台列表并恢复默认选中</summary>
    private void LoadPlatforms()
    {
        Platforms.Clear();
        foreach (var p in _platformService.GetAll())
        {
            if (!p.Enabled)
                continue;
            Platforms.Add(new PlatformItem(p, _iconService.GetIconPath(p), _iconService.GetAccentColor(p)));
        }

        var defaultId = _config.LoadSettings().Behavior.DefaultPlatformId;
        SelectedPlatform = Platforms.FirstOrDefault(x => x.Id == defaultId) ?? Platforms.FirstOrDefault();
    }

    /// <summary>平台数据变更后重建列表（尽量保留当前选中）</summary>
    public void RefreshPlatforms()
    {
        var selectedId = SelectedPlatform?.Id;
        LoadPlatforms();
        if (selectedId is not null)
            SelectedPlatform = Platforms.FirstOrDefault(x => x.Id == selectedId) ?? Platforms.FirstOrDefault();
    }

    /// <summary>拖拽排序：把 dragged 插入到 target 之前或之后，并重排底层列表后持久化（触发主界面刷新）</summary>
    public void ReorderPlatform(PlatformItem dragged, PlatformItem target, bool insertAfter)
    {
        if (dragged == null || target == null || dragged == target)
            return;
        var from = Platforms.IndexOf(dragged);
        var to = Platforms.IndexOf(target);
        if (from < 0 || to < 0)
            return;

        Platforms.RemoveAt(from);
        var insertAt = insertAfter ? to : to;
        // 移除后索引已前移：若源在目标之前，目标实际位置需 -1
        if (from < to)
            insertAt = to - 1;
        Platforms.Insert(insertAt, dragged);
        try
        {
            _platformService.Save(Platforms.Select(p => p.Info).ToList());
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("平台排序保存失败", ex);
            RefreshPlatforms();
        }
    }

    /// <summary>折叠 / 展开侧边栏：折叠态只显示图标，展开态显示图标+平台名</summary>
    private void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
    }

    /// <summary>切换到上一个平台（循环：首项跳到末项）</summary>
    public void SelectPrevPlatform()
    {
        if (Platforms.Count == 0) return;
        var idx = SelectedPlatform is null ? 0 : Platforms.IndexOf(SelectedPlatform);
        idx = (idx - 1 + Platforms.Count) % Platforms.Count;
        SelectedPlatform = Platforms[idx];
    }

    /// <summary>切换到下一个平台（循环：末项跳到首项）</summary>
    public void SelectNextPlatform()
    {
        if (Platforms.Count == 0) return;
        var idx = SelectedPlatform is null ? -1 : Platforms.IndexOf(SelectedPlatform);
        if (idx < 0) idx = 0; // 无选中时从首项开始
        else idx = (idx + 1) % Platforms.Count;
        SelectedPlatform = Platforms[idx];
    }

    /// <summary>设置（操作）窗口是否已打开</summary>
    public bool IsSettingsWindowOpen => _settingsWindow is { IsVisible: true };

    /// <summary>打开/关闭设置（操作）窗口：已打开则关闭，否则打开（非模态，便于 Alt+S 再次关闭）</summary>
    private SettingsWindow? _settingsWindow;
    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Close();
            return;
        }
        try
        {
            var window = ContainerLocator.Container.Resolve<SettingsWindow>();
            window.Owner = Application.Current.MainWindow;
            _settingsWindow = window;
            window.Closed += (_, _) => _settingsWindow = null;
            window.Show();
        }
        catch (Exception ex)
        {
            // 防御：窗口构造/显示若抛异常（如资源缺失、依赖解析失败），必须显式提示，
            // 绝不能静默失败导致「点击设置毫无反应」。异常详情已进日志/桌面 dump。
            LoggerHelper.Error("打开设置窗口失败", ex);
            _ = MessageBoxHelper.Error("打开设置失败：" + ex.Message);
        }
    }
}
