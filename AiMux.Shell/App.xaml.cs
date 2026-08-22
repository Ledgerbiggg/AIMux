using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using AiMux.Common.Config;
using AiMux.Common.Hotkey;
using AiMux.Models;
using AiMux.Common.Logger;
using AiMux.Services.IService;
using AiMux.Services.Service;
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

    private Mutex? _mutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 全局异常兜底：记录日志 + dump 完整堆栈到桌面文件，方便定位闪退
        DispatcherUnhandledException += (_, args) =>
        {
            DumpCrash(args.Exception, "UI 线程未处理异常");
            LoggerHelper.Error("UI 线程未处理异常", args.Exception);
            args.Handled = true; // 吞掉，不让程序闪退
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            DumpCrash(args.ExceptionObject as Exception, "AppDomain 未处理异常");
            LoggerHelper.Error("AppDomain 未处理异常", args.ExceptionObject as Exception);
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

    /// <summary>
    /// 把未处理异常完整堆栈写到桌面 AiMux-crash.txt，方便用户/开发定位闪退原因。
    /// 任何未能被吞掉的异常都会留下痕迹，不会再“看不到报错”。
    /// </summary>
    private static void DumpCrash(Exception? ex, string tag)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var path = Path.Combine(desktop, "AiMux-crash.txt");
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag}");
            sb.AppendLine(ex?.ToString() ?? "（无异常对象）");
            sb.AppendLine(new string('-', 60));
            File.AppendAllText(path, sb.ToString());
        }
        catch
        {
            // 写桌面失败就放弃，绝不在异常处理里再抛异常
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

    /// <summary>向已运行实例发送唤出消息（按窗口标题查找主窗口）</summary>
    private static void NotifyMainWindow()
    {
        var hwnd = FindWindow(null, "AI Chat Hub");
        if (hwnd != IntPtr.Zero)
            PostMessage(hwnd, WmShowInstance, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
