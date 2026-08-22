using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AiMux.Models;
using AiMux.Services.IService;
using AiMux.Shell.Util;
using Prism.Commands;
using Prism.Mvvm;

namespace AiMux.Shell.ViewModels.Settings;

/// <summary>平台管理面板：平台列表增删改、编辑表单、图标自动获取/上传</summary>
public class SettingsPlatformViewModel : BindableBase
{
    private readonly IPlatformService _platformService;
    private readonly IIconService _iconService;

    /// <summary>平台列表（对齐原型 plat-list）</summary>
    public ObservableCollection<PlatformInfo> Platforms { get; } = [];

    private PlatformInfo? _selected;
    public PlatformInfo? Selected
    {
        get => _selected;
        set
        {
            SetProperty(ref _selected, value);
            RaisePropertyChanged(nameof(HasSelection));
            LoadForm();
        }
    }

    public bool HasSelection => Selected is not null;

    // ---- 表单字段（对齐原型 form-card） ----
    private string _name = "";
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    private string _url = "";
    public string Url { get => _url; set => SetProperty(ref _url, value); }

    private string _iconLink = "";
    public string IconLink
    {
        get => _iconLink;
        set
        {
            if (SetProperty(ref _iconLink, value) && IsUrl(value))
                IconPreview = LoadPreview(value); // 粘贴链接即时预览，无需保存
        }
    }

    private bool _enabled = true;
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

    /// <summary>图标预览（缓存到本地后展示）</summary>
    private ImageSource? _iconPreview;
    public ImageSource? IconPreview
    {
        get => _iconPreview;
        private set => SetProperty(ref _iconPreview, value);
    }

    public DelegateCommand AddCommand { get; }
    public DelegateCommand DeleteCommand { get; }
    public DelegateCommand SaveCommand { get; }
    public DelegateCommand AutoFetchIconCommand { get; }

    public SettingsPlatformViewModel(IPlatformService platformService, IIconService iconService)
    {
        _platformService = platformService;
        _iconService = iconService;

        AddCommand = new DelegateCommand(AddPlatform);
        DeleteCommand = new DelegateCommand(DeletePlatform);
        SaveCommand = new DelegateCommand(SavePlatform);
        AutoFetchIconCommand = new DelegateCommand(AutoFetchIcon);

        foreach (var p in _platformService.GetAll())
            Platforms.Add(p);
        Selected = Platforms.FirstOrDefault();

        // 订阅平台数据变更：主界面拖拽调整顺序后，本面板实时同步刷新
        _platformService.PlatformsChanged += OnPlatformsChanged;
    }

    private void OnPlatformsChanged(object? sender, EventArgs e)
    {
        // 回到 UI 线程重新加载，保留当前选中项
        System.Windows.Application.Current.Dispatcher.Invoke(Reload);
    }

    /// <summary>从服务重新加载平台列表（保留当前选中 Id）</summary>
    public void Reload()
    {
        var selectedId = Selected?.Id;
        Platforms.Clear();
        foreach (var p in _platformService.GetAll())
            Platforms.Add(p);
        if (selectedId is not null)
            Selected = Platforms.FirstOrDefault(x => x.Id == selectedId) ?? Platforms.FirstOrDefault();
    }

    /// <summary>取消事件订阅，避免设置窗口关闭后内存泄漏与重复刷新</summary>
    public void Cleanup()
    {
        _platformService.PlatformsChanged -= OnPlatformsChanged;
    }

    /// <summary>选中平台变化时回填表单</summary>
    private void LoadForm()
    {
        if (Selected is null)
        {
            Name = Url = IconLink = "";
            Enabled = true;
            IconPreview = null;
            return;
        }
        Name = Selected.Name;
        Url = Selected.Url;
        // 若已配置的是图片链接则回填到链接框，本地路径则清空链接框（由预览展示）
        IconLink = IsUrl(Selected.Icon) ? Selected.Icon : "";
        Enabled = Selected.Enabled;
        IconPreview = LoadPreview(Selected.Icon);
    }

    /// <summary>持久化当前列表顺序（拖拽排序后调用），触发 PlatformsChanged 让主界面侧边栏同步刷新</summary>
    public void SavePlatformsOrder()
    {
        _platformService.Save(Platforms.ToList());
    }

    /// <summary>新增平台（默认选中进入编辑）</summary>
    private void AddPlatform()
    {
        var platform = new PlatformInfo
        {
            Id = _platformService.GenerateId(),
            Name = "新平台",
            Url = "https://",
            Enabled = true,
        };
        Platforms.Add(platform);
        Selected = platform;
        _ = MessageBoxHelper.Info("已新增平台，请编辑名称、地址与图标后点击「保存」。");
    }

    /// <summary>删除选中平台并落盘</summary>
    private void DeletePlatform()
    {
        if (Selected is null)
            return;
        Platforms.Remove(Selected);
        _platformService.Save(Platforms.ToList());
        Selected = Platforms.FirstOrDefault();
        if (Selected is null)
            LoadForm();
        _ = MessageBoxHelper.Info("已删除该平台。");
    }

    /// <summary>表单字段写回选中平台并落盘（图标：填了链接则存链接，否则保留原本地图标）</summary>
    private void SavePlatform()
    {
        if (Selected is null)
            return;
        Selected.Name = Name.Trim();
        Selected.Url = Url.Trim();
        // 图标链接优先：填了 http(s) 链接则作为 icon，主界面可直接加载；否则保留已抓取的本地 favicon
        var link = IconLink.Trim();
        if (!string.IsNullOrEmpty(link))
            Selected.Icon = link;
        Selected.Enabled = Enabled;
        _platformService.Save(Platforms.ToList());
        _ = MessageBoxHelper.Info("平台已保存。");
    }

    /// <summary>自动抓取 favicon（异步，成功后更新预览并清空链接框，因为已改用本地图标）</summary>
    private async void AutoFetchIcon()
    {
        if (Selected is null)
            return;
        var path = await _iconService.AutoFetchAsync(Selected);
        if (!string.IsNullOrEmpty(path))
        {
            Selected.Icon = path;
            IconPreview = LoadPreview(path);
            IconLink = "";
        }
    }

    /// <summary>加载图标预览：支持本地文件与 http(s) 图片链接</summary>
    private static ImageSource? LoadPreview(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        try
        {
            if (IsUrl(path))
                return new BitmapImage(new Uri(path, UriKind.Absolute));
            if (File.Exists(path))
                return new BitmapImage(new Uri(path, UriKind.Absolute));
        }
        catch
        {
            // 图标无效时忽略
        }
        return null;
    }

    /// <summary>判断字符串是否为 http(s) 图片链接</summary>
    private static bool IsUrl(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return false;
        return s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }
}
