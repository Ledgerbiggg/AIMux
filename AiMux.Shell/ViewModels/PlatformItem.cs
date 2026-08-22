using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AiMux.Models;
using Prism.Mvvm;

namespace AiMux.Shell.ViewModels;

/// <summary>侧边栏平台项：包装 PlatformInfo，提供界面所需的展示属性（图标/兜底色/首字母/角标）</summary>
public class PlatformItem : BindableBase
{
    /// <summary>原始平台配置</summary>
    public PlatformInfo Info { get; }

    public string Id => Info.Id;
    public string Name => Info.Name;

    /// <summary>首字母（无图标时兜底显示）</summary>
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";

    /// <summary>角标文本，如"代理"</summary>
    public string Badge => Info.Badge;

    /// <summary>是否显示角标</summary>
    public bool HasBadge => !string.IsNullOrEmpty(Badge);

    /// <summary>图标兜底底色画刷：默认透明（需求：底色默认不给，未配置即为透明）</summary>
    public Brush AccentBrush { get; }

    /// <summary>图标源（本地文件或图片链接），无则为 null</summary>
    public ImageSource? IconSource { get; }

    public PlatformItem(PlatformInfo info, string iconPath, string accentColor)
    {
        Info = info;
        AccentBrush = ParseBrush(accentColor);
        if (!string.IsNullOrEmpty(iconPath))
        {
            try
            {
                // http(s) 链接直接作为图标（需求：支持图片链接），本地路径需文件存在
                if (iconPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    iconPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    IconSource = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
                }
                else if (File.Exists(iconPath))
                {
                    IconSource = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
                }
            }
            catch
            {
                // 图标无效时忽略，走首字母兜底
            }
        }
    }

    /// <summary>解析 #RRGGBB 为画刷，空或失败回退透明（需求：默认不给底色）</summary>
    private static Brush ParseBrush(string hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color color)
                return new SolidColorBrush(color);
        }
        catch
        {
            // 非法颜色字符串回退
        }
        return new SolidColorBrush(Colors.Transparent);
    }
}
