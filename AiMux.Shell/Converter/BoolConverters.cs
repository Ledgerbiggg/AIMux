using System.Globalization;
using System.Windows;
using System.Windows.Data;
using AiMux.Models;

namespace AiMux.Shell.Converter;

/// <summary>bool 取反后转 Visibility（true → Collapsed）</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>bool 取反（绑定折叠状态反值用）</summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>侧边栏折叠时列表/控件居中（true → Center，false → Stretch 占满宽度）</summary>
public class BoolToHorizontalAlignmentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is HorizontalAlignment.Center;
}

/// <summary>bool 转为 double：parameter 格式 "展开,折叠"
/// 例如 "224,60" 表示展开 224、折叠 60
/// 用于侧边栏宽度绑定到 IsSidebarCollapsed，避免写死 Width 被动画覆盖</summary>
public class BoolToDoubleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "224,60").Split(',');
        if (parts.Length != 2) return 224.0;
        var s = value is true ? parts[1] : parts[0];
        return double.TryParse(s, NumberStyles.Any, culture, out var d) ? d : 224.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>HotkeyAction 转中文描述</summary>
public class HotkeyActionDescConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not HotkeyAction action) return "";
        return action switch
        {
            HotkeyAction.ToggleWindow => "呼出/隐藏主窗口",
            HotkeyAction.ToggleSidebar => "展开/折叠侧边栏",
            HotkeyAction.ToggleSize => "切换大窗/小窗尺寸",
            HotkeyAction.ToggleSettings => "打开/关闭设置窗口",
            HotkeyAction.PrevPlatform => "切换到上一个平台（循环）",
            HotkeyAction.NextPlatform => "切换到下一个平台（循环）",
            // 已停用的旧动作：不再出现在设置页，仅旧配置里残留数字时兜底显示
            HotkeyAction.ToggleMiniMode => "已停用：摸鱼模式改用 Esc 进/出，不再使用热键",
            HotkeyAction.ToggleMiniOrientation => "摸鱼模式切换横屏/竖屏",
            HotkeyAction.WebBack => "网页后退（浏览器历史上一层）",
            HotkeyAction.WebForward => "网页前进（浏览器历史下一层）",
            _ => action.ToString(),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}