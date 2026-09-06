using System.Windows;
using System.Windows.Controls;
using AiMux.Shell.ViewModels.Settings;

namespace AiMux.Shell.Views.Settings;

/// <summary>配置同步面板：WebDAV 配置同步的 PasswordBox 双向同步辅助</summary>
public partial class SettingsSyncView : UserControl
{
    public SettingsSyncView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>视图加载时从 ViewModel 回填密码到 PasswordBox（WPF PasswordBox 不支持 Binding）</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsSyncViewModel vm)
        {
            // 回填密码：切换 tab 回来时 PasswordBox 是空的，需要从 VM 恢复
            if (PwdBox.Password != vm.Password)
                PwdBox.Password = vm.Password;
        }
    }

    /// <summary>视图卸载前把 PasswordBox 当前值写回 ViewModel，防止视图被销毁后丢失</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsSyncViewModel vm)
            vm.Password = PwdBox.Password;
    }

    /// <summary>PasswordBox 不支持直接 Binding，通过 PasswordChanged 事件手动同步到 ViewModel</summary>
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsSyncViewModel vm)
            vm.Password = PwdBox.Password;
    }
}
