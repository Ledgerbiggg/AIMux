using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using AiMux.Common.Config;
using AiMux.Common.Hotkey;
using AiMux.Common.Logger;
using AiMux.Models;
using AiMux.Services.IService;
using AiMux.Shell.Controls;
using AiMux.Shell.ViewModels;
using AiMux.Shell.ViewModels.Settings;
using Wpf.Ui.Controls;

namespace AiMux.Shell.Views;

/// <summary>主窗口：侧边栏平台切换、WebView2 实例保活、全局热键、托盘与响应式布局编排</summary>
public partial class MainWindow : FluentWindow
{
    /// <summary>单实例唤出消息（第二实例发送，收到后显示窗口）</summary>
    private const int WmShowInstance = 0x0401;

    private readonly MainViewModel _vm;
    private readonly IPlatformService _platformService;
    private readonly IWebViewService _webViewService;
    private readonly IIconService _iconService;
    private readonly HotkeyManager _hotkeyManager;
    private readonly ITrayService _trayService;
    private readonly ConfigService _config;

    /// <summary>平台 Id → WebView 宿主（常驻不销毁，保留各平台上下文）</summary>
    private readonly Dictionary<string, WebViewHost> _hosts = [];

    private AppSettings _settings;
    private HwndSource? _hwndSource;
    private bool _closingToTray = true;
    private bool _isCompactMode;
    /// <summary>置顶（盯住窗口）状态：仅当前会话内有效，不持久化</summary>
    private bool _isPinned;
    /// <summary>复制按钮反馈计时器：复制成功后显示打勾，约 2 秒后恢复图标</summary>
    private readonly DispatcherTimer _copyTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    /// <summary>正在切换平台标志：防止 RefreshPlatforms 重建时 SelectedPlatform 变化递归触发死循环</summary>
    private bool _isSwitching;

    /// <summary>是否处于摸鱼模式（无边框小窗）</summary>
    private bool _isMiniMode;

    /// <summary>摸鱼模式自己的置顶状态（与主窗口 📌 独立，互不覆盖）</summary>
    private bool _isMiniPinned;

    /// <summary>进入摸鱼模式时的窗口快照，退出时逐项还原</summary>
    private MiniSnapshot? _miniSnapshot;

    /// <summary>摸鱼模式保留的缩放热区厚度（DIP）：WebView2 会吞掉鼠标消息，
    /// 必须靠"窗口自身的这几像素空隙"命中 WM_NCHITTEST 才能拖拽缩放</summary>
    private const double MiniResizeEdge = 6;

    /// <summary>窗口宽度低于此阈值自动折叠侧栏，高于则自动展开（仅记录自动状态，避免反复覆盖手动操作）</summary>
    private const double SidebarAutoCollapseWidth = 560;

    /// <summary>上一次自动折叠状态，仅在跨越阈值时切换 IsSidebarCollapsed</summary>
    private bool _autoCollapsed;

    public MainWindow(MainViewModel vm, IPlatformService platformService, IWebViewService webViewService,
        IIconService iconService, HotkeyManager hotkeyManager, ITrayService trayService, ConfigService config)
    {
        InitializeComponent();
        // 容错设置窗口图标：失败也不影响窗口显示，绝不抛异常导致闪退
        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/app-icon_ico_128x128.ico", UriKind.Absolute);
            Icon = new System.Windows.Media.Imaging.BitmapImage(iconUri);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("设置窗口图标失败（已忽略）", ex);
        }
        _vm = vm;
        _platformService = platformService;
        _webViewService = webViewService;
        _iconService = iconService;
        _hotkeyManager = hotkeyManager;
        _trayService = trayService;
        _config = config;
        _settings = _config.LoadSettings();
        // 一次性迁移旧默认热键。必须放在订阅 SettingsSaved 之前：
        // 迁移内部会保存配置并触发 SettingsSaved，否则会递归回到 RegisterHotkey
        MigrateLegacyHotkeys();
        DataContext = vm;

        _copyTimer.Tick += (_, _) => ResetCopyButton();
        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.ReloadRequested += (_, _) => ReloadCurrent();
        _vm.HomeRequested += (_, _) => GoHome();
        _trayService.ShowRequested += (_, _) => ToggleWindow();   // 右键菜单：显示/隐藏（切换）
        _trayService.OpenRequested += (_, _) => ShowWindow();    // 左键单击托盘：仅打开，不隐藏（不与快捷键串）
        _trayService.ExitRequested += (_, _) => ExitApp();
        _platformService.PlatformsChanged += (_, _) => _vm.RefreshPlatforms();

        // 设置保存后（热键/外观/窗口行为等）重新加载、重注册热键并刷新按钮显隐
        _config.SettingsSaved += (_, _) =>
        {
            _settings = _config.LoadSettings();
            RegisterHotkey();
            ApplyButtonVisibility();
        };

        ApplySavedWindowState();
        ApplyButtonVisibility();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 消息钩子/托盘/热键已在 OnSourceInitialized 装配（静默启动时不经过 Show，Loaded 不会触发），
        // 这里只负责首次平台加载与界面状态初始化
        try
        {
            // 首次加载默认平台：先让 WebView 初始化，界面先出来
            if (_vm.SelectedPlatform is not null)
                await SwitchPlatformAsync(_vm.SelectedPlatform);

            // 启动静默检查版本：有新版本时标题栏显示更新标识（失败/无更新静默）
            _ = _vm.CheckUpdateAtStartupAsync();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("OnLoaded 加载平台期间异常", ex);
        }

        // 同步右上角主题按钮图标：当前为深色显示🌙，浅色显示☀
        if (ThemeToggleIcon != null)
            ThemeToggleIcon.Text = _settings.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? "🌙" : "☀";
    }

    /// <summary>窗口句柄创建后触发（首次 Show 或静默启动的 EnsureHandle 均会触发）：
    /// 装配消息钩子、托盘图标与全局热键。这些必须在 SourceInitialized 就绪——
    /// 静默启动窗口从未显示、Loaded 不会触发，若仍放在 OnLoaded 会导致托盘/热键全部失效，窗口无法呼出</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // 挂载窗口消息钩子：处理全局热键与单实例唤出。
        // 静默启动时视觉树尚未布局，PresentationSource.FromVisual 可能取不到，必须用 FromHwnd
        var hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        // 子类化窗口过程：处理顶部边缘缩放（见 SubclassWndProc 注释）
        try
        {
            _subclassProc = SubclassWndProc;
            _oldWndProc = SetWindowLongPtrSafe(hwnd, GwlWndProc,
                Marshal.GetFunctionPointerForDelegate(_subclassProc));
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("子类化窗口过程失败（顶部缩放修复不生效，其余功能不受影响）", ex);
        }

        // 托盘图标尽早显示：静默启动时这是用户唯一的可见入口（失败不阻塞启动）
        try { _trayService.Show(); }
        catch (Exception ex) { LoggerHelper.Error("托盘图标显示失败", ex); }

        // 静默启动的 Visibility=Hidden / ShowInTaskbar=false 已由 App.InitializeShell
        // 在 EnsureHandle 之前设置好（句柄创建后再改样式会闪任务栏图标），此处无需处理

        // 热键注册整体容错，绝不阻塞界面；静默启动用户靠热键呼出窗口，必须在此注册
        try { RegisterHotkey(); }
        catch (Exception ex) { LoggerHelper.Error("RegisterHotkey 异常", ex); }
    }

    /// <summary>窗口消息处理：WM_HOTKEY 由 HotkeyManager 经 HotkeyPressed 事件分发到对应动作，
    /// 这里只标记已处理（分发逻辑在 OnHotkeyPressed 中按 Action 区分，避免 Alt+W 误触发开关窗口）。
    /// 注意：顶部缩放不在本 hook 处理——WPF-UI 的 WindowChrome hook 注册在先、先执行并截断消息，
    /// 这里收不到 WM_NCHITTEST，顶部命中统一由 SubclassWndProc（子类化）处理</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_hotkeyManager.HandleMessage(msg, wParam))
        {
            handled = true;
        }
        else if (msg == WmShowInstance)
        {
            handled = true;
            ToggleWindow();
        }
        return IntPtr.Zero;
    }

    /// <summary>旧版默认热键一次性迁移：早期版本 Alt+←/→ 是「切换平台」，
    /// 现在改为「网页后退/前进」，平台切换让位到 Alt+↑/↓。
    /// 只在用户从未自定义过这两项（仍等于旧默认值）且还没有后退配置时才改写，改完立即落盘；
    /// 之后一律以用户配置为准（想换回切平台可在设置页重新录制）</summary>
    private void MigrateLegacyHotkeys()
    {
        try
        {
            var list = _settings.Hotkeys;
            if (list is null || list.Count == 0)
                return;

            // 永久下线「打开设置」快捷键：清理旧配置残留的 ToggleSettings 绑定（幂等，删完立即落盘）
            if (list.RemoveAll(h => h.Action == HotkeyAction.ToggleSettings) > 0)
            {
                _config.SaveSettings(_settings);
                LoggerHelper.Info("已移除「打开设置」快捷键绑定：新版不再提供该热键");
            }
            // 以 WebForward 是否已存在作为"是否已迁移"的判据：
            // 上一版虽然已有 WebBack(Alt+Z)，但还没有 WebForward，也必须走迁移
            if (list.Any(h => h.Action == HotkeyAction.WebForward))
                return;

            static bool Is(HotkeyBinding b, string mod, string key) =>
                string.Equals((b.Modifier ?? "").Trim(), mod, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((b.Key ?? "").Trim(), key, StringComparison.OrdinalIgnoreCase);

            var prev = list.FirstOrDefault(h => h.Action == HotkeyAction.PrevPlatform);
            var next = list.FirstOrDefault(h => h.Action == HotkeyAction.NextPlatform);

            // 任何一项被用户改过，就认为他有自己的键位安排，该项保持不动
            if (prev is not null && !Is(prev, "Alt", "Left")) prev = null;
            if (next is not null && !Is(next, "Alt", "Right")) next = null;

            if (prev is not null) { prev.Modifier = "Alt"; prev.Key = "Up"; }
            if (next is not null) { next.Modifier = "Alt"; next.Key = "Down"; }

            // 清掉旧版本的 WebBack（曾是 Alt+Z），改为方向键方案
            list.RemoveAll(h => h.Action is HotkeyAction.WebBack or HotkeyAction.WebForward);
            list.Add(new HotkeyBinding { Action = HotkeyAction.WebBack, Modifier = "Alt", Key = "Left" });
            list.Add(new HotkeyBinding { Action = HotkeyAction.WebForward, Modifier = "Alt", Key = "Right" });

            _config.SaveSettings(_settings);
            LoggerHelper.Info("旧默认热键已迁移：Alt+←/→ 改为网页后退/前进，切换平台改为 Alt+↑/↓");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("热键迁移失败（不影响启动，可在设置页手动调整）", ex);
        }
    }

    /// <summary>注册所有全局热键（按 Action 注册多个），失败仅记录日志，绝不弹窗
    /// 在 OnLoaded 期间弹 MessageBox 会阻塞消息循环导致界面出不来</summary>
    private void RegisterHotkey()
    {
        try
        {
            _hotkeyManager.UnregisterAll();
            _hotkeyManager.HotkeyPressed -= OnHotkeyPressed;
            _hotkeyManager.HotkeyPressed += OnHotkeyPressed;

            var hwnd = new WindowInteropHelper(this).Handle;

            // 以内置默认热键为基准（保证默认 Alt+Q/W/←/→ 始终生效），
            // 再用用户配置中已存在对应动作的绑定覆盖，实现「默认可用 + 用户可改」
            var defaults = new Dictionary<HotkeyAction, HotkeyBinding>
            {
                [HotkeyAction.ToggleWindow] = new() { Action = HotkeyAction.ToggleWindow, Modifier = "Alt", Key = "Q" },
                [HotkeyAction.ToggleSidebar] = new() { Action = HotkeyAction.ToggleSidebar, Modifier = "Alt", Key = "E" },
                [HotkeyAction.ToggleSize] = new() { Action = HotkeyAction.ToggleSize, Modifier = "Alt", Key = "W" },
                [HotkeyAction.WebBack] = new() { Action = HotkeyAction.WebBack, Modifier = "Alt", Key = "Left" },
                [HotkeyAction.WebForward] = new() { Action = HotkeyAction.WebForward, Modifier = "Alt", Key = "Right" },
                [HotkeyAction.PrevPlatform] = new() { Action = HotkeyAction.PrevPlatform, Modifier = "Alt", Key = "Up" },
                [HotkeyAction.NextPlatform] = new() { Action = HotkeyAction.NextPlatform, Modifier = "Alt", Key = "Down" },
                [HotkeyAction.ToggleMiniOrientation] = new() { Action = HotkeyAction.ToggleMiniOrientation, Modifier = "Alt", Key = "R" },
            };
            if (_settings.Hotkeys != null)
            {
                foreach (var user in _settings.Hotkeys)
                {
                    if (user != null && defaults.TryGetValue(user.Action, out var def))
                    {
                        if (!string.IsNullOrEmpty(user.Key)) def.Modifier = user.Modifier;
                        if (!string.IsNullOrEmpty(user.Key)) def.Key = user.Key;
                    }
                }
            }

            // 注册 id 用 Action 枚举 int 值 + 偏移，保证唯一
            foreach (var binding in defaults.Values)
            {
                if (string.IsNullOrEmpty(binding.Key)) continue;
                try
                {
                    if (!_hotkeyManager.Register(hwnd, binding.Action.ToString(),
                            binding.Modifier, binding.Key, (int)binding.Action + 0x1000))
                    {
                        // 仅记录日志，不弹窗：设置页保存时会实时提示用户
                        LoggerHelper.Info($"热键注册失败 {binding.Action}: {binding.Modifier}+{binding.Key} — {_hotkeyManager.LastError}");
                    }
                }
                catch (Exception ex)
                {
                    // 单个热键注册异常不阻塞其他热键
                    LoggerHelper.Error($"注册热键异常 {binding.Action}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            // 整体兜底：任何异常都不阻塞应用启动
            LoggerHelper.Error("RegisterHotkey 整体异常", ex);
        }
    }

    /// <summary>热键触发：按 Action 名称分发到对应命令
    /// 支持窗口呼出/隐藏、侧边栏折叠、平台前后循环切换</summary>
    private void OnHotkeyPressed(object? sender, string actionId)
    {
        if (!Enum.TryParse<HotkeyAction>(actionId, out var action)) return;

        switch (action)
        {
            // 全局呼出/隐藏：即使窗口隐藏到托盘也要生效
            case HotkeyAction.ToggleWindow:
                ToggleWindow();
                break;

            // 以下动作仅在主窗口聚焦（软件打开、当前页）时生效；隐藏到托盘或未聚焦时不触发
            case HotkeyAction.ToggleSidebar:
                if (!IsActive) return;
                _vm.IsSidebarCollapsed = !_vm.IsSidebarCollapsed;
                break;
            case HotkeyAction.ToggleSize:
                if (!IsActive) return;
                ToggleCompact_Click(null!, null!);
                break;
            case HotkeyAction.PrevPlatform:
                if (!IsActive) return;
                _vm.SelectPrevPlatform();
                break;
            case HotkeyAction.NextPlatform:
                if (!IsActive) return;
                _vm.SelectNextPlatform();
                break;

            // 摸鱼模式横竖屏切换：仅在小窗内有效
            case HotkeyAction.ToggleMiniOrientation:
                if (!_isMiniMode) return;
                ToggleMiniOrientation();
                break;

            // 网页后退 / 前进：小窗里从视频页返回列表最常用（不占屏幕，靠热键实现）
            case HotkeyAction.WebBack:
                if (!IsActive) return;
                GoBackCurrent();
                break;
            case HotkeyAction.WebForward:
                if (!IsActive) return;
                GoForwardCurrent();
                break;
        }
    }

    /// <summary>ViewModel 属性变化：选中平台切换、侧边栏折叠动画
    /// _isSwitching 标志防止 RefreshPlatforms 重建集合时 SelectedPlatform 变化递归触发 SwitchPlatformAsync</summary>
    private async void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedPlatform))
        {
            if (_isSwitching) return; // 切换期间忽略，避免 RefreshPlatforms 重建引发死循环
            if (_vm.SelectedPlatform is not null)
                await SwitchPlatformAsync(_vm.SelectedPlatform);
        }
        else if (e.PropertyName == nameof(MainViewModel.IsSidebarCollapsed))
        {
            AnimateSidebar();
        }
    }

    /// <summary>切换平台：隐藏所有实例，显示目标实例（不存在则懒加载创建）
    /// _isSwitching 标志防止 RefreshPlatforms 重建时 SelectedPlatform 变化递归触发</summary>
    private async Task SwitchPlatformAsync(PlatformItem item)
    {
        _isSwitching = true;
        try
        {
            foreach (var h in _hosts.Values)
                h.Visibility = Visibility.Collapsed;

            if (!_hosts.TryGetValue(item.Id, out var host))
            {
                host = new WebViewHost(item.Info, _webViewService, _iconService, _platformService);
                host.AddressChanged += OnHostAddressChanged;
                _hosts[item.Id] = host;
                WebViewContainer.Children.Add(host);
            }

            host.Visibility = Visibility.Visible;
            // 摸鱼模式下切进来的实例（含刚懒加载创建的）同样要最小缩放。
            // 必须在 EnsureInitializedAsync 之前设置：WebView2 尚未创建时先记下请求，初始化后补应用
            if (_isMiniMode)
                host.ApplyMiniZoom();
            // Esc 订阅常开：大窗按 Esc 进入摸鱼、小窗按 Esc 退出（网页全屏时钩子不上报，不抢原生全屏键）
            SyncEscapeSubscription(host);
            await host.EnsureInitializedAsync();

            // 切回该平台（实例是复用的、不会重新加载）时重压一遍设置页里的缩放，
            // 保证"配置值是强制值"：之前在页面里临时缩放过的，切回来就恢复成配置值
            host.ApplyConfiguredZoom();

            // 切换平台后主动同步地址栏为当前实例的实际网址：已初始化实例不会重新导航，
            // 不会再触发 NavigationCompleted，必须在这里更新，否则会显示上一个平台的旧链接
            if (AddressBar != null)
                AddressBar.Text = host.CurrentUrl;
            _vm.CurrentActualUrl = host.CurrentUrl;
        }
        finally
        {
            _isSwitching = false;
        }
    }

    /// <summary>呼出/隐藏窗口切换（托盘双击、热键呼出均走此入口）
    /// 窗口已隐藏或最小化 → 呼出到前台
    /// 窗口在前台 → 隐藏到托盘
    /// 用 _isToggling 防止短时间内重复触发（热键 NoRepeat 不够，Toggle 可能被连续调用）</summary>
    private bool _isToggling;

    private void ToggleWindow()
    {
        if (_isToggling) return; // 防止 300ms 内重复触发
        _isToggling = true;
        Dispatcher.BeginInvoke(new Action(() => _isToggling = false),
            DispatcherPriority.ApplicationIdle);

        // 窗口已隐藏或最小化 → 呼出到前台
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            ShowWindow();
            return;
        }

        // 窗口可见但在后台（失焦）→ 激活到前台，不隐藏
        if (!IsActive)
        {
            Activate();
            return;
        }

        // 窗口在前台且正常显示 → 隐藏到托盘
        Hide();
    }

    /// <summary>显示并激活窗口：Show + 恢复正常状态 + Topmost 闪烁确保跳到最前面
    /// 随后自动聚焦当前平台输入框（失败不阻塞）</summary>
    private async void ShowWindow()
    {
        // 先恢复窗口状态再 Show，避免 Show 后又因 WindowState 异常隐藏
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Show();
        ShowInTaskbar = true;
        Activate();

        // Topmost 闪烁：确保窗口跳到所有窗口最前面（解决被其他窗口遮挡"闪一下消失"的问题）
        Topmost = true;
        Topmost = false;
        Topmost = _isMiniMode ? _isMiniPinned : _isPinned; // 恢复置顶状态（摸鱼小窗用自己的置顶开关）

        // 短暂等待页面响应后注入聚焦脚本（失败不阻塞，可手动点击）
        try
        {
            await Task.Delay(350);
            if (_vm.SelectedPlatform is not null &&
                _hosts.TryGetValue(_vm.SelectedPlatform.Id, out var host))
            {
                await host.FocusInputAsync();
            }
        }
        catch { /* 聚焦失败不影响窗口显示 */ }
    }

    /// <summary>刷新当前平台网页</summary>
    private void ReloadCurrent()
    {
        if (_vm.SelectedPlatform is not null &&
            _hosts.TryGetValue(_vm.SelectedPlatform.Id, out var host))
        {
            host.Reload();
        }
    }

    /// <summary>当前平台网页后退一层（浏览器历史记录）；无可后退项则静默忽略</summary>
    private void GoBackCurrent()
    {
        if (_vm.SelectedPlatform is not null &&
            _hosts.TryGetValue(_vm.SelectedPlatform.Id, out var host))
        {
            host.GoBack();
        }
    }

    /// <summary>当前平台网页前进一层（浏览器历史记录）；无可前进项则静默忽略</summary>
    private void GoForwardCurrent()
    {
        if (_vm.SelectedPlatform is not null &&
            _hosts.TryGetValue(_vm.SelectedPlatform.Id, out var host))
        {
            host.GoForward();
        }
    }

    /// <summary>回到当前平台配置的主页（Platform.Url）：仅在当前平台自身实例内导航，不影响其它平台常驻状态</summary>
    private void GoHome()
    {
        if (_vm.SelectedPlatform is not null &&
            _hosts.TryGetValue(_vm.SelectedPlatform.Id, out var host))
        {
            host.Navigate(_vm.SelectedPlatform.Info.Url);
        }
    }

    /// <summary>侧边栏宽度动画（224px ↔ 60px 图标态）</summary>
    private void AnimateSidebar()
    {
        var target = _vm.IsSidebarCollapsed ? 60.0 : 224.0;
        // 关键：动画前把 Width 固定为当前实际像素宽度（必须是 finite 值）。
        // 否则若 Width 为 NaN（Auto/未设），DoubleAnimation 取不到 origin 值会抛异常。
        if (double.IsNaN(SidebarBorder.Width) || SidebarBorder.Width <= 0)
            SidebarBorder.Width = SidebarBorder.ActualWidth > 0 ? SidebarBorder.ActualWidth : target;
        SidebarBorder.BeginAnimation(WidthProperty,
            new DoubleAnimation(SidebarBorder.Width, target, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
            });
    }

    /// <summary>响应式布局：窗口尺寸变化时同步保存窗口状态</summary>
    private void MainWindow_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 摸鱼小窗本身就是小尺寸：若走自动折叠逻辑会把侧栏标记改掉，退出后就还原不回原状态
        if (_isMiniMode)
            return;

        // 缩放窗口到一定宽度时自动折叠/展开侧栏：宽度不足阈值则合上，恢复则展开
        if (e.PreviousSize.Width == 0)
            return; // 初次布局不处理
        var shouldCollapse = Width < SidebarAutoCollapseWidth;
        if (shouldCollapse != _autoCollapsed)
        {
            _autoCollapsed = shouldCollapse;
            _vm.IsSidebarCollapsed = shouldCollapse;
        }
    }

    /// <summary>缩放按钮：切换窗口大 / 小尺寸，并联动侧边栏折叠状态
    /// 缩小窗口 → 侧栏收起；放大窗口 → 侧栏展开。侧栏的单独折叠按钮仍只管侧栏</summary>
    private void ToggleCompact_Click(object sender, RoutedEventArgs e)
    {
        var win = _settings.Window;
        _isCompactMode = !_isCompactMode;
        Width = _isCompactMode ? win.CompactWidth : win.FullWidth;
        Height = _isCompactMode ? win.CompactHeight : win.FullHeight;
        // 切换尺寸后在「当前所在屏幕」内居中：用窗口句柄取所在显示器的 WorkArea，
        // 避免多屏时跳到主屏（如竖屏看视频却被甩到横屏）；并做边界约束防止超出屏幕
        var area = GetCurrentMonitorWorkArea();
        Left = Math.Max(area.Left, Math.Min(area.Left + (area.Width - Width) / 2, area.Right - Width));
        Top = Math.Max(area.Top, Math.Min(area.Top + (area.Height - Height) / 2, area.Bottom - Height));
        // 图标随模式切换：小窗显示放大(↗)，大窗显示缩小(↙)，明确表达按钮语义
        if (CompactToggleIcon != null)
            CompactToggleIcon.Text = _isCompactMode ? "↗" : "↙";
    }

    /// <summary>置顶（盯住窗口）按钮：仅当前会话内切换，不持久化到配置</summary>
    private void PinToggle_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        Topmost = _isPinned;
        if (PinToggleIcon != null)
            PinToggleIcon.Foreground = _isPinned
                ? (System.Windows.Media.Brush)FindResource("AccentTextFillColorPrimaryBrush")
                : (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush");
    }

    /// <summary>地址栏回车：补全协议后在当前 WebView 打开（新内容替换旧内容，通用）</summary>
    private void AddressBar_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var url = AddressBar?.Text?.Trim();
        if (string.IsNullOrEmpty(url)) return;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;
        if (_vm.SelectedPlatform is not null &&
            _hosts.TryGetValue(_vm.SelectedPlatform.Id, out var host))
        {
            host.Navigate(url);
        }
    }

    /// <summary>WebView 实际网址变化：仅当地址栏未聚焦时同步，避免打断用户输入</summary>
    private void OnHostAddressChanged(string url)
    {
        if (AddressBar != null && !AddressBar.IsKeyboardFocused && url != AddressBar.Text)
            AddressBar.Text = url;
        // 同步实际网址，驱动主页按钮"已在主页则禁用"的可用状态
        _vm.CurrentActualUrl = url;
    }

    /// <summary>复制当前链接到剪贴板；成功后图标变打勾并高亮，约 2 秒后恢复</summary>
    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        var url = AddressBar?.Text?.Trim();
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            Clipboard.SetText(url);
            // 视觉反馈：变成打勾状态，2 秒后恢复，明确告知用户复制成功
            if (CopyUrlIcon != null)
            {
                CopyUrlIcon.Text = "✓";
                CopyUrlIcon.Foreground = (System.Windows.Media.Brush)FindResource("AccentTextFillColorPrimaryBrush");
            }
            _copyTimer.Stop();
            _copyTimer.Start();
        }
        catch (Exception ex)
        {
            LoggerHelper.Info($"复制链接失败: {ex.Message}");
        }
    }

    /// <summary>复制反馈结束：图标与颜色恢复初始状态</summary>
    private void ResetCopyButton()
    {
        _copyTimer.Stop();
        if (CopyUrlIcon != null)
        {
            CopyUrlIcon.Text = "📋";
            CopyUrlIcon.Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush");
        }
    }

    /// <summary>右上角主题切换：在浅色 / 深色间切换，立即应用并保存（配置中的主题同步更新）</summary>
    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        var next = _settings.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
        _settings.Theme = next;
        SettingsAppearanceViewModel.ApplyTheme(next);
        if (ThemeToggleIcon != null)
            ThemeToggleIcon.Text = next.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? "🌙" : "☀";
        try { _config.SaveSettings(_settings); }
        catch (Exception ex) { LoggerHelper.Error("主题切换保存异常", ex); }
    }

    /// <summary>关闭按钮 → 隐藏到托盘常驻（真正退出走托盘"退出"）</summary>
    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        // 调试：记录主窗口收到关闭请求时的调用来源
        AiMux.Common.Logger.LoggerHelper.Info($"MainWindow_OnClosing 触发, _closingToTray={_closingToTray}\n{Environment.StackTrace}");
        if (_closingToTray)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        SaveWindowState();
        // 还原子类化窗口过程，避免窗口销毁期间消息进入已失效的委托
        if (_oldWndProc != IntPtr.Zero)
        {
            try
            {
                SetWindowLongPtrSafe(new WindowInteropHelper(this).Handle, GwlWndProc, _oldWndProc);
            }
            catch { /* 还原失败随窗口销毁无实际影响 */ }
            _oldWndProc = IntPtr.Zero;
        }
        _hwndSource?.RemoveHook(WndProc);
        _hotkeyManager.Dispose();
    }

    /// <summary>恢复上次窗口位置与大小模式</summary>
    private void ApplySavedWindowState()
    {
        var win = _settings.Window;
        if (win.RememberPosition && win.Left is not null && win.Top is not null)
        {
            Left = win.Left.Value;
            Top = win.Top.Value;
        }
        _isCompactMode = win.IsCompact;
        Width = _isCompactMode ? win.CompactWidth : win.FullWidth;
        Height = _isCompactMode ? win.CompactHeight : win.FullHeight;
        // 侧边栏折叠状态独立记忆（不随窗口大小变化），默认折叠
        _vm.IsSidebarCollapsed = win.SidebarCollapsed;
    }

    /// <summary>记录窗口位置与大小模式到 settings.json</summary>
    private void SaveWindowState()
    {
        var win = _settings.Window;

        // 摸鱼模式下窗口处于小窗尺寸：不能把它当成主窗口尺寸写进记忆，
        // 只记小窗自己的位置与朝向，主窗口的尺寸/位置保持上次退出摸鱼时的值
        if (_isMiniMode)
        {
            if (WindowState == WindowState.Normal)
            {
                win.MiniLeft = Left;
                win.MiniTop = Top;
            }
            win.MiniTopmost = _isMiniPinned;
            _config.SaveSettings(_settings);
            return;
        }

        if (WindowState == WindowState.Normal)
        {
            win.Left = Left;
            win.Top = Top;
        }
        win.IsCompact = _isCompactMode;
        win.SidebarCollapsed = _vm.IsSidebarCollapsed;
        _config.SaveSettings(_settings);
    }

    /// <summary>托盘"退出"：真正结束进程</summary>
    private void ExitApp()
    {
        _closingToTray = false;
        SaveWindowState();
        _trayService.Hide();
        Application.Current.Shutdown();
    }

    /// <summary>按设置应用主界面导航条按钮显隐（外观设置页可配置）。
    /// 用 Collapsed 而非 Hidden：隐藏的按钮不占位，其余按钮左移补齐</summary>
    private void ApplyButtonVisibility()
    {
        try
        {
            var ui = _settings.Ui;
            if (CopyUrlButton != null) CopyUrlButton.Visibility = ui.ShowCopyUrl ? Visibility.Visible : Visibility.Collapsed;
            if (HomeButton != null) HomeButton.Visibility = ui.ShowHome ? Visibility.Visible : Visibility.Collapsed;
            if (CompactToggleButton != null) CompactToggleButton.Visibility = ui.ShowCompactToggle ? Visibility.Visible : Visibility.Collapsed;
            if (ReloadButton != null) ReloadButton.Visibility = ui.ShowReload ? Visibility.Visible : Visibility.Collapsed;
            if (ThemeToggleButton != null) ThemeToggleButton.Visibility = ui.ShowThemeToggle ? Visibility.Visible : Visibility.Collapsed;
            if (PinToggleButton != null) PinToggleButton.Visibility = ui.ShowPinToggle ? Visibility.Visible : Visibility.Collapsed;
            if (MiniModeButton != null) MiniModeButton.Visibility = ui.ShowMiniMode ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("应用按钮显隐设置失败（保持默认显示）", ex);
        }
    }

    #region 摸鱼模式（无边框小窗）

    /// <summary>进入摸鱼模式前的窗口状态快照，退出时逐项还原</summary>
    private sealed class MiniSnapshot
    {
        public double Width, Height, Left, Top, MinWidth, MinHeight;
        public Thickness BorderThickness, WebViewMargin;
        public bool SidebarCollapsed, MainPinned;
        public WindowState State;
    }

    /// <summary>摸鱼模式开关（导航条 🐟 按钮）</summary>
    private void MiniMode_Click(object sender, RoutedEventArgs e)
    {
        if (_isMiniMode) ExitMiniMode();
        else EnterMiniMode();
    }

    /// <summary>进入摸鱼模式：隐藏标题栏/侧栏/导航条并缩成无边框小窗。
    /// 关键点是"就地变形"而不是新建窗口——WebView2 实例原地保留，
    /// 正在播放的视频不中断、登录态不丢、全局热键也无需重新注册</summary>
    private void EnterMiniMode()
    {
        if (_isMiniMode) return;
        try
        {
            var win = _settings.Window;

            // 1) 快照：先记下原始窗口状态（最大化要记原值），再归一化为 Normal 便于改尺寸
            var prevState = WindowState;
            if (prevState != WindowState.Normal)
                WindowState = WindowState.Normal;
            _miniSnapshot = new MiniSnapshot
            {
                Width = Width, Height = Height, Left = Left, Top = Top,
                MinWidth = MinWidth, MinHeight = MinHeight,
                BorderThickness = BorderThickness,
                WebViewMargin = WebViewContainer.Margin,
                SidebarCollapsed = _vm.IsSidebarCollapsed,
                MainPinned = _isPinned,
                State = prevState,
            };

            _isMiniMode = true;

            // 2) 隐藏所有"桌面形态"元素，只留网页（顶部只保留 8px 隐形拖动条，不占观看区域）
            TitleBar.Visibility = Visibility.Collapsed;
            SidebarBorder.Visibility = Visibility.Collapsed;
            NavBarBorder.Visibility = Visibility.Collapsed;
            MiniTopBar.Visibility = Visibility.Visible;
            // 左/右/下各留 6px：WebView2 是原生子窗口会吞掉其覆盖区域的鼠标消息，
            // 只有窗口自身露出的这几像素才能命中 WM_NCHITTEST 完成拖拽缩放（顶部 8px 归拖动带）
            WebViewContainer.Margin = new Thickness(MiniResizeEdge, 0, MiniResizeEdge, MiniResizeEdge);

            // 3) 去边框：Mica / 圆角关掉，WPF 边框厚度归零，再用 DWM 抹掉 Win11 的 1px 系统描边
            WindowBackdropType = WindowBackdropType.None;
            WindowCornerPreference = WindowCornerPreference.DoNotRound;
            BorderThickness = new Thickness(0);
            ApplyDwmBorderless(true);

            // 4) 先放宽 Min 约束再改尺寸——顺序反了目标尺寸会被旧 Min 夹断
            MinWidth = 200;
            MinHeight = 120;
            ApplyMiniSize(keepCenter: false);

            // 5) 页面缩放到 WebView2 允许的最小值：CSS 视口被放大到 ~1920px 宽，
            //    桌面版页面正好铺满小窗，竖版滚动条消失（这是摸鱼观看体验的关键）
            //    （Esc 订阅已在每个实例创建时挂好，常开，此处无需处理）
            foreach (var host in _hosts.Values)
            {
                host.ApplyMiniZoom();
            }

            // 6) 摸鱼时默认置顶，免得被工作窗口盖住（想关掉改 settings.json 的 MiniTopmost）
            _isMiniPinned = win.MiniTopmost;
            Topmost = _isMiniPinned;

            LoggerHelper.Info($"进入摸鱼模式（{(win.MiniIsPortrait ? "竖屏" : "横屏")} {Width}x{Height}）");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("进入摸鱼模式失败", ex);
            _isMiniMode = false;
        }
    }

    /// <summary>退出摸鱼模式：还原进入前的一切（尺寸/位置/侧栏/置顶/边框/窗口状态），
    /// 同时记住本次小窗的朝向与位置，下次进入原样恢复</summary>
    private void ExitMiniMode()
    {
        if (!_isMiniMode) return;
        try
        {
            // 记住本次小窗位置与朝向
            var win = _settings.Window;
            if (WindowState == WindowState.Normal)
            {
                win.MiniLeft = Left;
                win.MiniTop = Top;
            }
            win.MiniTopmost = _isMiniPinned;
            try { _config.SaveSettings(_settings); }
            catch (Exception ex) { LoggerHelper.Error("保存摸鱼模式状态失败", ex); }

            _isMiniMode = false;

            // 还原界面元素
            TitleBar.Visibility = Visibility.Visible;
            SidebarBorder.Visibility = Visibility.Visible;
            NavBarBorder.Visibility = Visibility.Visible;
            MiniTopBar.Visibility = Visibility.Collapsed;
            WebViewContainer.Margin = _miniSnapshot?.WebViewMargin ?? new Thickness(0, 0, 8, 8);

            // 还原网页缩放（回到平台配置的比例）。Esc 订阅保持常开：大窗按 Esc 还能再次进入摸鱼
            foreach (var host in _hosts.Values)
            {
                host.RestoreZoom();
            }

            // 还原边框外观
            WindowBackdropType = WindowBackdropType.Mica;
            WindowCornerPreference = WindowCornerPreference.Default;
            BorderThickness = _miniSnapshot?.BorderThickness ?? new Thickness(0);
            ApplyDwmBorderless(false);

            var snap = _miniSnapshot;
            if (snap is not null)
            {
                // 同样先恢复 Min 约束再恢复尺寸
                MinWidth = snap.MinWidth;
                MinHeight = snap.MinHeight;
                Topmost = snap.MainPinned;
                Width = snap.Width;
                Height = snap.Height;
                Left = snap.Left;
                Top = snap.Top;
                _vm.IsSidebarCollapsed = snap.SidebarCollapsed;
                if (WindowState != snap.State)
                    WindowState = snap.State;
            }
            _miniSnapshot = null;
            _isMiniPinned = false;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("退出摸鱼模式失败", ex);
        }
    }

    /// <summary>应用摸鱼小窗尺寸：keepCenter 为 true 时保持窗口中心点不动（横竖屏切换用），
    /// 否则优先使用上次记忆的小窗位置</summary>
    private void ApplyMiniSize(bool keepCenter)
    {
        var win = _settings.Window;
        var cx = Left + Width / 2;
        var cy = Top + Height / 2;

        Width = win.MiniIsPortrait ? win.MiniPortraitWidth : win.MiniLandscapeWidth;
        Height = win.MiniIsPortrait ? win.MiniPortraitHeight : win.MiniLandscapeHeight;

        if (keepCenter)
        {
            Left = cx - Width / 2;
            Top = cy - Height / 2;
        }
        else if (win.MiniLeft is not null && win.MiniTop is not null)
        {
            Left = win.MiniLeft.Value;
            Top = win.MiniTop.Value;
        }

        // 记忆位置可能落在已拔掉的显示器或改过分辨率的屏幕上，统一夹回可视区
        ClampToVisibleArea();
    }

    /// <summary>横竖屏切换（Alt+R）：保持窗口中心点不动，避免跳屏</summary>
    private void ToggleMiniOrientation()
    {
        if (!_isMiniMode) return;
        try
        {
            var win = _settings.Window;
            win.MiniIsPortrait = !win.MiniIsPortrait;
            ApplyMiniSize(keepCenter: true);
            try { _config.SaveSettings(_settings); }
            catch (Exception ex) { LoggerHelper.Error("保存摸鱼朝向失败", ex); }
            LoggerHelper.Info($"摸鱼模式切换到{(win.MiniIsPortrait ? "竖屏" : "横屏")} {Width}x{Height}");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("切换摸鱼朝向失败", ex);
        }
    }

    /// <summary>把窗口夹回可视区（多屏拔插后记忆位置可能失效）。
    /// 按"目标矩形所在显示器"取 WorkArea——摸鱼小窗常被丢到副屏，若按窗口当前屏幕约束会被硬拽回主屏</summary>
    private void ClampToVisibleArea()
    {
        try
        {
            var area = GetWorkAreaForRect(Left, Top, Width, Height);
            // Math.Max 兜底：窗口比工作区还大时（如超小副屏）直接对齐左上角，不产生负范围
            Left = Math.Max(area.Left, Math.Min(Left, Math.Max(area.Left, area.Right - Width)));
            Top = Math.Max(area.Top, Math.Min(Top, Math.Max(area.Top, area.Bottom - Height)));
        }
        catch (Exception ex)
        {
            LoggerHelper.Info($"窗口位置约束失败: {ex.Message}");
        }
    }

    /// <summary>取指定矩形（DIP）所在显示器的 WorkArea（DIP）。
    /// 坐标换算按窗口当前 DPI 近似，取不到显示器时回退到窗口当前所在屏幕</summary>
    private Rect GetWorkAreaForRect(double left, double top, double width, double height)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var dpi = hwnd == IntPtr.Zero ? 96u : GetDpiForWindow(hwnd);
            if (dpi == 0) dpi = 96;
            var scale = dpi / 96.0;

            var rc = new RECT
            {
                left = (int)Math.Round(left * scale),
                top = (int)Math.Round(top * scale),
                right = (int)Math.Round((left + width) * scale),
                bottom = (int)Math.Round((top + height) * scale),
            };
            var mon = MonitorFromRect(ref rc, MONITOR_DEFAULTTONEAREST);
            if (mon != IntPtr.Zero)
            {
                var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(mon, ref info))
                {
                    var wa = info.rcWork;
                    return new Rect(wa.left / scale, wa.top / scale,
                        (wa.right - wa.left) / scale, (wa.bottom - wa.top) / scale);
                }
            }
        }
        catch { /* 回退到当前屏幕 */ }
        return GetCurrentMonitorWorkArea();
    }

    /// <summary>顶部热区：按住拖动整窗；右键退出摸鱼模式（双击太容易在拖动时误触，弃用）。
    /// WebView2 会吞掉其覆盖区域的鼠标消息，整窗拖动只能在它上方这条 8px 热区上做</summary>
    private void MiniDragStrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isMiniMode) return;

        try { DragMove(); }
        catch (Exception ex) { LoggerHelper.Info($"拖动小窗失败: {ex.Message}"); }
    }

    /// <summary>拖动条右键：退出摸鱼模式（鼠标不用离开视频区域，也不会与拖动冲突）</summary>
    private void MiniDragStrip_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isMiniMode) return;
        e.Handled = true;
        ExitMiniMode();
    }

    /// <summary>窗口级 Esc：摸鱼模式进出总开关（大窗进入 / 小窗退出）。
    /// 焦点不在网页里时（如点了拖动条、导航条）由这里兜底；网页内的 Esc 由 WebViewHost 的 JS 钩子上报</summary>
    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        if (_isMiniMode) ExitMiniMode();
        else EnterMiniMode();
    }

    /// <summary>网页内按 Esc 的回调（来自 WebViewHost JS 钩子上报）：大窗进入摸鱼、小窗退出摸鱼</summary>
    private void OnEscapeRequested()
    {
        if (_isMiniMode) ExitMiniMode();
        else EnterMiniMode();
    }

    /// <summary>订阅某个 WebViewHost 的 Esc 上报（幂等）。订阅常开：
    /// 大窗按 Esc 进入摸鱼、小窗按 Esc 退出；网页全屏时 JS 钩子不上报，不抢网页全屏的退出键</summary>
    private void SyncEscapeSubscription(WebViewHost host)
    {
        host.EscapePressed -= OnEscapeRequested;
        host.EscapePressed += OnEscapeRequested;
    }

    #endregion 摸鱼模式（无边框小窗）

    #region 顶部边缘缩放（子类化窗口过程）

    /// <summary>非客户区命中测试消息</summary>
    private const int WmNcHitTest = 0x0084;

    /// <summary>命中结果常量：客户区 / 左边 / 右边 / 顶边 / 左上角 / 右上角 / 底边 / 左下角 / 右下角</summary>
    private const int HtClient = 1, HtLeft = 10, HtRight = 11, HtTop = 12,
        HtTopLeft = 13, HtTopRight = 14, HtBottom = 15, HtBottomLeft = 16, HtBottomRight = 17;

    /// <summary>DWM 窗口边框颜色属性与其特殊取值（用于抹掉 Win11 的 1px 系统描边）</summary>
    private const int DwmwaBorderColor = 34;
    private const uint DwmwaColorNone = 0xFFFFFFFE;
    private const uint DwmwaColorDefault = 0xFFFFFFFF;

    /// <summary>窗口过程替换索引</summary>
    private const int GwlWndProc = -4;

    /// <summary>顶部缩放边缘厚度（DIP），与内容区右/下留出的 8px 缩放缝保持一致</summary>
    private const double TopResizeEdge = 8;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>子类化过程委托引用：必须持有防止被 GC 回收导致崩溃</summary>
    private WndProcDelegate? _subclassProc;

    /// <summary>原窗口过程地址</summary>
    private IntPtr _oldWndProc;

    /// <summary>子类化窗口过程：先于 WPF 的 HwndSource hook 链收到消息。
    /// 顶部无法缩放的根因：WPF-UI 的 TitleBar 控件以 IsHitTestVisibleInChrome 接管整条标题栏，
    /// WindowChrome 的元素命中检查优先于 resize 边框判定，顶边（含边缘 4px）都返回 HTCLIENT，
    /// 永远轮不到缩放命中（左/右/底边无 TitleBar 遮挡所以正常）。
    /// 此处在更早的层面拦截 WM_NCHITTEST，顶边 8px 返回 HTTOP 系列恢复上下缩放</summary>
    private IntPtr SubclassWndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        // 摸鱼小窗：系统边框已全部去掉，缩放热区只能由这里手工提供。
        // 左/右/下边与三个角返回缩放命中；顶部中间必须返回 HTCLIENT——
        // 那块 8px 是 WPF 的拖动带，要留给它接收鼠标才能拖动整个无边框窗口
        if (msg == WmNcHitTest && _isMiniMode && WindowState == WindowState.Normal)
        {
            try
            {
                if (GetWindowRect(hWnd, out var mrc))
                {
                    var edge = (int)Math.Ceiling(MiniResizeEdge * GetDpiForWindow(hWnd) / 96.0);
                    var w = mrc.right - mrc.left;
                    var h = mrc.bottom - mrc.top;
                    var x = (short)(lParam.ToInt64() & 0xFFFF) - mrc.left;
                    var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF) - mrc.top;
                    var atLeft = x <= edge;
                    var atRight = x >= w - edge;
                    var atTop = y <= edge;
                    var atBottom = y >= h - edge;

                    if (atTop && atLeft) return (IntPtr)HtTopLeft;
                    if (atTop && atRight) return (IntPtr)HtTopRight;
                    if (atBottom && atLeft) return (IntPtr)HtBottomLeft;
                    if (atBottom && atRight) return (IntPtr)HtBottomRight;
                    if (atLeft) return (IntPtr)HtLeft;
                    if (atRight) return (IntPtr)HtRight;
                    if (atBottom) return (IntPtr)HtBottom;
                    if (atTop) return (IntPtr)HtClient; // 顶部中间 = 拖动带，交回 WPF 处理
                }
            }
            catch { /* 异常时回落到默认处理 */ }
            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }

        if (msg == WmNcHitTest && WindowState == WindowState.Normal && ResizeMode != ResizeMode.NoResize)
        {
            try
            {
                if (GetWindowRect(hWnd, out var rc))
                {
                    // 按 DPI 把边缘厚度换算为物理像素，与实际命中区域一致
                    var edge = (int)Math.Ceiling(TopResizeEdge * GetDpiForWindow(hWnd) / 96.0);
                    // lParam 低/高 16 位为鼠标屏幕像素坐标（有符号）
                    var relX = (short)(lParam.ToInt64() & 0xFFFF) - rc.left;
                    var relY = (short)((lParam.ToInt64() >> 16) & 0xFFFF) - rc.top;
                    if (relY <= edge)
                    {
                        if (relX <= edge) return (IntPtr)HtTopLeft;
                        if (relX >= rc.right - rc.left - edge) return (IntPtr)HtTopRight;
                        return (IntPtr)HtTop;
                    }
                }
            }
            catch { /* 异常时交回原过程按默认逻辑处理 */ }
        }
        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    /// <summary>跨位数安全的 SetWindowLong(Ptr)：32 位进程无 SetWindowLongPtr 导出</summary>
    private static IntPtr SetWindowLongPtrSafe(IntPtr hWnd, int nIndex, IntPtr value)
        => IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, value)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>开关 DWM 绘制的窗口描边：Win11 上即使 BackdropType=None 仍会留 1px 浅色边框，
    /// 摸鱼小窗靠它做到真正无边框；Win10 不支持该属性，调用失败直接忽略（回退到默认外观）</summary>
    private void ApplyDwmBorderless(bool borderless)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            var color = borderless ? DwmwaColorNone : DwmwaColorDefault;
            _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref color, sizeof(uint));
        }
        catch (Exception ex)
        {
            LoggerHelper.Info($"设置 DWM 边框颜色失败: {ex.Message}");
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint value, int size);

    #endregion

    #region 多屏定位辅助

    /// <summary>取窗口当前所在显示器的 WorkArea（任务栏之外的可用区域），多屏时不会跳到主屏</summary>
    private Rect GetCurrentMonitorWorkArea()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (mon != IntPtr.Zero)
            {
                var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(mon, ref info))
                {
                    var wa = info.rcWork;
                    return new Rect(wa.left, wa.top, wa.right - wa.left, wa.bottom - wa.top);
                }
            }
        }
        catch { /* 回退到主屏 WorkArea */ }
        var area = SystemParameters.WorkArea;
        return new Rect(area.X, area.Y, area.Width, area.Height);
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    #endregion

    #region 侧边栏平台列表拖拽排序

    /// <summary>拖拽状态：源项、源容器（用于半透明反馈）、当前目标容器（用于高亮反馈）、插入位置</summary>
    private PlatformItem? _dragItem;
    private ListBoxItem? _dragSourceContainer;
    private ListBoxItem? _dropTargetContainer;
    private bool _insertAfter;
    private Point _dragStartPoint;

    private void PlatformsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _dragItem = GetPlatformItemFromEvent(sender as ListBox, e.OriginalSource as DependencyObject);
    }

    private void PlatformsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem == null)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetDragVisual();
            return;
        }
        var diff = e.GetPosition(null) - _dragStartPoint;
        if (Math.Abs(diff.X) > 4 || Math.Abs(diff.Y) > 4)
        {
            var lb = sender as ListBox;
            if (lb != null)
            {
                // 拖拽开始：源项半透明，给出明确的拖拽视觉反馈
                _dragSourceContainer = GetContainerFromPlatform(lb, _dragItem);
                if (_dragSourceContainer != null)
                    _dragSourceContainer.Opacity = 0.4;
                DragDrop.DoDragDrop(lb, _dragItem, DragDropEffects.Move);
                ResetDragVisual();
            }
        }
    }

    /// <summary>拖拽悬停：根据鼠标在目标项的上/下半决定插入到前还是后，并高亮目标项</summary>
    private void PlatformsList_DragOver(object sender, DragEventArgs e)
    {
        var lb = sender as ListBox;
        var target = GetPlatformItemFromEvent(lb, e.OriginalSource as DependencyObject);
        var container = target == null ? null : GetContainerFromPlatform(lb, target);
        if (container == null)
        {
            ClearDropHighlight();
            return;
        }
        // 鼠标在目标项上半区 → 插到前面；下半区 → 插到后面
        var pos = e.GetPosition(container);
        _insertAfter = pos.Y > container.ActualHeight / 2;
        if (_dropTargetContainer != container)
        {
            ClearDropHighlight();
            _dropTargetContainer = container;
            container.Background = new SolidColorBrush(Color.FromArgb(40, 0, 120, 212));
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void PlatformsList_Drop(object sender, DragEventArgs e)
    {
        var dragged = _dragItem;
        var lb = sender as ListBox;
        var target = GetPlatformItemFromEvent(lb, e.OriginalSource as DependencyObject);
        ResetDragVisual();
        if (dragged != null && target != null && _vm != null)
            _vm.ReorderPlatform(dragged, target, _insertAfter);
        e.Handled = true;
    }

    /// <summary>清除拖拽过程中的所有临时视觉状态</summary>
    private void ResetDragVisual()
    {
        if (_dragSourceContainer != null)
            _dragSourceContainer.Opacity = 1;
        ClearDropHighlight();
        _dragItem = null;
        _dragSourceContainer = null;
    }

    private void ClearDropHighlight()
    {
        if (_dropTargetContainer != null)
            _dropTargetContainer.Background = null;
        _dropTargetContainer = null;
    }

    /// <summary>从鼠标命中的内部元素向上回溯到 ListBoxItem，再取绑定的 PlatformItem</summary>
    private PlatformItem? GetPlatformItemFromEvent(ListBox? lb, DependencyObject? source)
    {
        if (lb == null || source == null)
            return null;
        var container = source;
        while (container != null && !(container is ListBoxItem))
            container = VisualTreeHelper.GetParent(container);
        if (container is ListBoxItem item && item.Content is PlatformItem pi)
            return pi;
        return null;
    }

    /// <summary>根据 PlatformItem 找到对应的 ListBoxItem 容器（用于设置透明度/高亮）</summary>
    private ListBoxItem? GetContainerFromPlatform(ListBox? lb, PlatformItem pi)
    {
        if (lb == null)
            return null;
        foreach (var item in lb.Items)
        {
            if (item is PlatformItem p && p.Id == pi.Id)
                return lb.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
        }
        return null;
    }

    #endregion
}
