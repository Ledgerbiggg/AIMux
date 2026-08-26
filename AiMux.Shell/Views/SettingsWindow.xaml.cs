using AiMux.Shell.ViewModels;
using Wpf.Ui.Controls;

namespace AiMux.Shell.Views;

/// <summary>设置窗口：左侧导航 + 四个设置面板（平台管理/通用/热键/外观）</summary>
public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        // 容错设置窗口图标：失败也不影响窗口显示，绝不抛异常导致打不开
        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/app-icon_ico_128x128.ico", UriKind.Absolute);
            Icon = new System.Windows.Media.Imaging.BitmapImage(iconUri);
        }
        catch (Exception ex)
        {
            // 忽略：图标缺失不影响功能
            System.Diagnostics.Debug.WriteLine("设置窗口图标加载失败（已忽略）: " + ex.Message);
        }
        DataContext = viewModel;
        Closed += (_, _) => viewModel.PlatformVm.Cleanup();
        // 调试：记录设置窗口关闭请求的调用来源
        Closing += (_, _) => AiMux.Common.Logger.LoggerHelper.Info($"SettingsWindow_OnClosing 触发\n{Environment.StackTrace}");
    }
}
