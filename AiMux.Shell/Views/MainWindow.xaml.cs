using System.ComponentModel;
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
    /// <summary>正在切换平台标志：防止 RefreshPlatforms 重建时 SelectedPlatform 变化递归触发死循环</summary>
    private bool _isSwitching;

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
        DataContext = vm;

        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.ReloadRequested += (_, _) => ReloadCurrent();
        _trayService.ShowRequested += (_, _) => ToggleWindow();   // 右键菜单：显示/隐藏（切换）
        _trayService.OpenRequested += (_, _) => ShowWindow();    // 左键单击托盘：仅打开，不隐藏（不与快捷键串）
        _trayService.ExitRequested += (_, _) => ExitApp();
        _platformService.PlatformsChanged += (_, _) => _vm.RefreshPlatforms();

        // 设置保存后（热键/窗口行为等）重新加载并重注册热键
        _config.SettingsSaved += (_, _) =>
        {
            _settings = _config.LoadSettings();
            RegisterHotkey();
        };

        ApplySavedWindowState();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 整体 try-catch：OnLoaded 期间任何异常都不能导致闪退或界面出不来
        try
        {
            // 挂载窗口消息钩子：处理全局热键与单实例唤出
            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            _hwndSource?.AddHook(WndProc);

            _trayService.Show();

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

        // 热键注册整体容错，绝不阻塞界面
        try { RegisterHotkey(); }
        catch (Exception ex) { LoggerHelper.Error("RegisterHotkey 异常", ex); }

        // 同步右上角主题按钮图标：当前为深色显示🌙，浅色显示☀
        if (ThemeToggleIcon != null)
            ThemeToggleIcon.Text = _settings.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? "🌙" : "☀";
    }

    /// <summary>窗口句柄创建后（显示前）触发：若配置了启动到托盘，直接隐藏，避免主界面闪一下</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (_settings.Behavior.StartHidden)
        {
            // 此时窗口尚未显示，Hide 不会闪；App.InitializeShell 在 StartHidden 时不会 Show 主窗口
            Visibility = Visibility.Hidden;
            ShowInTaskbar = false;
        }
    }

    /// <summary>窗口消息处理：WM_HOTKEY 由 HotkeyManager 经 HotkeyPressed 事件分发到对应动作，
    /// 这里只标记已处理（分发逻辑在 OnHotkeyPressed 中按 Action 区分，避免 Alt+W 误触发开关窗口）</summary>
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
                [HotkeyAction.ToggleSettings] = new() { Action = HotkeyAction.ToggleSettings, Modifier = "Alt", Key = "S" },
                [HotkeyAction.PrevPlatform] = new() { Action = HotkeyAction.PrevPlatform, Modifier = "Alt", Key = "Left" },
                [HotkeyAction.NextPlatform] = new() { Action = HotkeyAction.NextPlatform, Modifier = "Alt", Key = "Right" },
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
            // 打开/关闭设置：仅当本软件（主窗口）可见，或设置窗口已打开时响应；
            // 主窗口隐藏到托盘时不触发，避免关掉主界面后还能调出设置
            case HotkeyAction.ToggleSettings:
                if (!this.IsVisible && !_vm.IsSettingsWindowOpen)
                    return;
                ((System.Windows.Input.ICommand)_vm.OpenSettingsCommand).Execute(null);
                break;
            case HotkeyAction.PrevPlatform:
                if (!IsActive) return;
                _vm.SelectPrevPlatform();
                break;
            case HotkeyAction.NextPlatform:
                if (!IsActive) return;
                _vm.SelectNextPlatform();
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
                _hosts[item.Id] = host;
                WebViewContainer.Children.Add(host);
            }

            host.Visibility = Visibility.Visible;
            await host.EnsureInitializedAsync();
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

    /// <summary>WebView 区域边缘缩放：在右侧 / 下侧 / 右下角 6px 内拦截鼠标，
    /// 绕过 WebView2 吞掉系统 resize 的问题，实现整屏宽度 / 高度拖拽缩放</summary>
    private const double ResizeEdge = 6;
    private bool _resizing;
    private bool _resizeRight, _resizeBottom;
    private Point _resizeStart;
    private double _startWidth, _startHeight;

    private void WebViewContainer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        var pt = e.GetPosition(this);
        bool right = pt.X >= ActualWidth - ResizeEdge;
        bool bottom = pt.Y >= ActualHeight - ResizeEdge;

        if (_resizing && e.LeftButton == MouseButtonState.Pressed)
        {
            var now = e.GetPosition(this);
            if (_resizeRight)
                Width = Math.Max(MinWidth, _startWidth + (now.X - _resizeStart.X));
            if (_resizeBottom)
                Height = Math.Max(MinHeight, _startHeight + (now.Y - _resizeStart.Y));
            e.Handled = true;
            return;
        }

        if (right && bottom) this.Cursor = Cursors.SizeNWSE;
        else if (right) this.Cursor = Cursors.SizeWE;
        else if (bottom) this.Cursor = Cursors.SizeNS;
        else if (this.Cursor != Cursors.Arrow) this.Cursor = Cursors.Arrow;
    }

    private void WebViewContainer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pt = e.GetPosition(this);
        bool right = pt.X >= ActualWidth - ResizeEdge;
        bool bottom = pt.Y >= ActualHeight - ResizeEdge;
        if (right || bottom)
        {
            _resizing = true;
            _resizeRight = right;
            _resizeBottom = bottom;
            _resizeStart = e.GetPosition(this);
            _startWidth = Width;
            _startHeight = Height;
            e.Handled = true; // 阻止事件传入 WebView，避免误触网页
            Mouse.Capture(this);
        }
    }

    private void WebViewContainer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizing)
        {
            _resizing = false;
            _resizeRight = _resizeBottom = false;
            Mouse.Capture(null);
            this.Cursor = Cursors.Arrow;
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
        // 切换尺寸后居中到屏幕（位置复原），避免窗口被拖到角落
        var area = SystemParameters.WorkArea;
        Left = (area.Width - Width) / 2 + area.Left;
        Top = (area.Height - Height) / 2 + area.Top;
        // 联动侧边栏：缩小收起、放大展开（动画由 OnVmPropertyChanged 触发）
        _vm.IsSidebarCollapsed = _isCompactMode;
        // 图标随模式切换：小窗显示放大(↗)，大窗显示缩小(↙)，明确表达按钮语义
        if (CompactToggleIcon != null)
            CompactToggleIcon.Text = _isCompactMode ? "↗" : "↙";
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
        // 小窗模式初始即折叠侧边栏（动画由 OnVmPropertyChanged 触发）
        _vm.IsSidebarCollapsed = _isCompactMode;
    }

    /// <summary>记录窗口位置与大小模式到 settings.json</summary>
    private void SaveWindowState()
    {
        var win = _settings.Window;
        if (WindowState == WindowState.Normal)
        {
            win.Left = Left;
            win.Top = Top;
        }
        win.IsCompact = _isCompactMode;
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
