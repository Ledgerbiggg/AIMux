using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using AiMux.Common.Config;
using AiMux.Common.Hotkey;
using AiMux.Models;
using AiMux.Common.Logger;
using AiMux.Services.IService;
using AiMux.Services.Service;
using AiMux.Shell.Util;
using AiMux.Shell.ViewModels;
using AiMux.Shell.ViewModels.Settings;
using AiMux.Shell.Views;
using Prism.Ioc;
using Prism.Unity;

namespace AiMux.Shell;

/// <summary>应用入口：Prism 依赖注入、单实例保护、主题应用</summary>
public partial class App : PrismApplication
{
    /// <summary>单实例唤出消息（与主窗口 WndProc 约定一致）</summary>
    private const int WmShowInstance = 0x0401;

    /// <summary>主窗口标题：第二实例按标题 FindWindow 查找已运行实例（与 MainViewModel.Title 保持一致）</summary>
    private const string MainWindowTitle = "AI Chat Hub";

    private Mutex? _mutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 全局异常兜底：记录日志 + 弹窗提示，完整堆栈写入日志目录（不写桌面）
        DispatcherUnhandledException += (_, args) =>
        {
            DumpCrash(args.Exception, "UI 线程未处理异常");
            LoggerHelper.Error("UI 线程未处理异常", args.Exception);
            ShowCrashDialog(args.Exception, "UI 线程未处理异常");
            args.Handled = true; // 吞掉，不让程序闪退
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            DumpCrash(args.ExceptionObject as Exception, "AppDomain 未处理异常");
            LoggerHelper.Error("AppDomain 未处理异常", args.ExceptionObject as Exception);
            ShowCrashDialog(args.ExceptionObject as Exception, "AppDomain 未处理异常");
        };

        // 单实例：二次启动时通知已运行实例呼出窗口，自身退出
        _mutex = new Mutex(true, "AiMux_SingleInstance", out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            NotifyMainWindow();
            Shutdown();
            return;
        }
        // 托盘常驻应用的窗口生命周期必须显式管理：默认的 OnLastWindowClose 会在
        // "所有窗口都已关闭/隐藏"时直接把整个应用 Shutdown——典型触发场景是
        // 主窗口已用 Alt+Q 或关闭按钮隐藏到托盘，此时设置窗口是唯一可见窗口，
        // 关掉它（尤其前面还弹过 MessageBox）就会把主窗口和托盘图标一起带走。
        // 改为只由显式 Shutdown（托盘"退出"、更新安装）结束进程
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 只有真正持有 mutex 的进程才能释放，否则会抛同步异常
        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
            _mutex?.Dispose();
        }
        base.OnExit(e);
    }

    /// <summary>弹窗防递归标志：弹窗自身触发的渲染/布局异常会被全局处理器再次捕获，避免死循环</summary>
    private static bool _isShowingCrashDialog;

    /// <summary>弹出未处理异常提示框，详情完整堆栈已在日志中（日志位置一并告知用户）</summary>
    private static void ShowCrashDialog(Exception? ex, string tag)
    {
        if (_isShowingCrashDialog)
            return;
        _isShowingCrashDialog = true;
        try
        {
            var detail = ex is null ? "（无异常对象）" : $"{ex.GetType().Name}: {ex.Message}";
            var msg = $"{tag}：\n\n{detail}\n\n详细信息已写入日志：{LoggerHelper.LogDir}";
            _ = MessageBoxHelper.Error(msg, "AiMux 异常提示")
                .ContinueWith(_ => _isShowingCrashDialog = false,
                    TaskScheduler.FromCurrentSynchronizationContext());
        }
        catch
        {
            // 弹窗失败就放弃，绝不在异常处理里再抛异常
            _isShowingCrashDialog = false;
        }
    }

    /// <summary>
    /// 把未处理异常完整堆栈写入日志目录 crash.log（不写桌面），方便定位闪退原因。
    /// 任何未能被吞掉的异常都会留下痕迹，不会再“看不到报错”。
    /// </summary>
    private static void DumpCrash(Exception? ex, string tag)
    {
        try
        {
            Directory.CreateDirectory(LoggerHelper.LogDir);
            var path = Path.Combine(LoggerHelper.LogDir, "crash.log");
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag}");
            sb.AppendLine(ex?.ToString() ?? "（无异常对象）");
            sb.AppendLine(new string('-', 60));
            File.AppendAllText(path, sb.ToString());
        }
        catch
        {
            // 写日志失败就放弃，绝不在异常处理里再抛异常
        }
    }

    /// <summary>依赖注入注册：基础设施单例 + 服务 + 窗口/ViewModel</summary>
    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        // 配置与日志（ConfigService 同时负责 %AppData%\AiMux 目录初始化）
        var config = new ConfigService();
        LoggerHelper.SetLogDir(config.LogsDir);
        LoggerHelper.Info($"应用启动，配置目录: {config.RootDir}");
        containerRegistry.RegisterInstance(config);

        // 业务服务
        containerRegistry.RegisterSingleton<IPlatformService, PlatformService>();
        containerRegistry.RegisterSingleton<IIconService, IconService>();
        containerRegistry.RegisterSingleton<IWebViewService, WebViewService>();
        containerRegistry.RegisterSingleton<ITrayService, TrayService>();
        containerRegistry.RegisterSingleton<IWebDavService, WebDavService>();
        containerRegistry.RegisterSingleton<HotkeyManager>();

        // 窗口与 ViewModel
        containerRegistry.RegisterSingleton<MainViewModel>();
        containerRegistry.RegisterSingleton<SettingsViewModel>();
        containerRegistry.RegisterSingleton<MainWindow>();
        // SettingsWindow 用瞬态注册：WPF Window 关闭后不能再次 Show/ShowDialog，
        // 每次打开都需要新实例，ViewModel 用 Singleton 保持状态
        containerRegistry.Register<SettingsWindow>();
    }

    /// <summary>创建主窗口前应用保存的主题；首次启动生成默认 settings.json</summary>
    protected override Window? CreateShell()
    {
        var config = Container.Resolve<ConfigService>();
        var settings = config.LoadSettings();
        if (!File.Exists(config.SettingsPath))
            config.SaveSettings(settings);
        try
        {
            SettingsAppearanceViewModel.ApplyTheme(settings.Theme);
        }
        catch (Exception ex)
        {
            // 主题应用失败不阻塞启动（WPF-UI 兼容性问题兜底）
            LoggerHelper.Error("应用主题失败", ex);
        }
        return Container.Resolve<MainWindow>();
    }

    /// <summary>静默启动（StartHidden）时绝不调用 Show：Prism 默认实现会先 Show 主窗口，
    /// 再由窗口把自身隐藏，Win32 层面窗口已被绘制出来，表现为"闪一下才消失"。
    /// 这里改为只创建窗口句柄（EnsureHandle 不显示窗口），窗口从未显示过，天然无闪烁；
    /// 句柄由 MainWindow.OnSourceInitialized 用于装配托盘、全局热键与单实例唤出消息</summary>
    protected override void InitializeShell(Window shell)
    {
        var config = Container.Resolve<ConfigService>();
        if (config.LoadSettings().Behavior.StartHidden)
        {
            // 保留主窗口引用（与 Prism 默认行为一致），托盘退出/单实例唤出依赖它
            MainWindow = shell;
            // 关键：WPF 创建 HWND 时只要 Visibility 属性为 Visible（默认值），
            // 无论走 Show 还是 EnsureHandle，CreateWindowEx 都会带上 WS_VISIBLE 样式，
            // 窗口创建即显示——这正是"EnsureHandle 了却照样弹窗"的原因。
            // 必须在 EnsureHandle 之前置为 Hidden，HWND 才真正不可见，实现零闪烁
            shell.Visibility = Visibility.Hidden;
            // 任务栏图标也要在句柄创建前关掉：句柄创建后再改样式，任务栏图标可能闪现一下
            shell.ShowInTaskbar = false;
            // 只创建 HWND 不显示窗口：会触发 SourceInitialized（装配钩子/托盘/热键），不触发 Loaded
            var handle = new WindowInteropHelper(shell).EnsureHandle();
            // 静默窗口不经过布局，Title 绑定不会求值，HWND 标题为空——
            // 第二实例 FindWindow 将找不到它，双击图标无法唤出。这里手动补上标题
            SetWindowText(handle, MainWindowTitle);
            return;
        }
        base.InitializeShell(shell);
    }

    /// <summary>Prism 默认在 OnInitialized 里调用 MainWindow?.Show()——
    /// 静默启动"闪一下再隐藏"的真正元凶（InitializeShell 默认实现并不 Show，Show 藏在这一步）。
    /// 只拦 InitializeShell 不够：隐藏的窗口会被这里的 Show 强行拉出来闪一下，
    /// 随后又被 Visibility=Hidden 压回隐藏。静默启动时必须连这里一起跳过</summary>
    /// <summary>AutoSync 防重入标志：上传进行中不重复触发（Interlocked 保证线程安全）</summary>
    private static int _autoSyncBusy;

    protected override void OnInitialized()
    {
        // AutoSync：用户勾选「保存设置时自动上传 WebDAV」后，任何设置保存（热键/外观/通用/
        // 同步页）落盘都会自动把最新配置同步到云端。挂载必须在静默启动 return 之前（与窗口显隐无关）。
        // 防递归已由链路保证：上传成功记录同步时间是静默写盘（raiseEvent=false，不再触发本事件）
        var config = Container.Resolve<ConfigService>();
        var webDav = Container.Resolve<IWebDavService>();
        config.SettingsSaved += (_, _) =>
        {
            var s = config.LoadSettings();
            if (!s.WebDav.AutoSync || string.IsNullOrWhiteSpace(s.WebDav.ServerUrl)) return;
            if (Interlocked.Exchange(ref _autoSyncBusy, 1) == 1) return; // 上传中，跳过本次
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await webDav.UploadAsync();
                    LoggerHelper.Info($"自动同步 WebDAV：{(result.Ok ? "成功" : result.Message)}");
                }
                catch (Exception ex)
                {
                    LoggerHelper.Error("自动同步 WebDAV 异常", ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _autoSyncBusy, 0);
                }
            });
        };

        if (config.LoadSettings().Behavior.StartHidden)
        {
            return; // 不调 base：窗口从头到尾不被显示，零闪烁
        }
        base.OnInitialized();
    }

    /// <summary>向已运行实例发送唤出消息（按窗口标题查找主窗口）</summary>
    private static void NotifyMainWindow()
    {
        var hwnd = FindWindow(null, MainWindowTitle);
        if (hwnd != IntPtr.Zero)
            PostMessage(hwnd, WmShowInstance, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr hWnd, string text);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
