namespace AiMux.Models;

/// <summary>全局设置（对应 settings.json）</summary>
public class AppSettings
{
    /// <summary>全局热键列表：内置默认绑定（开箱即用）。
    /// 其中 ToggleWindow(Alt+Q) 为全局呼出/隐藏；其余动作仅在主窗口聚焦时生效（隐藏到托盘时不触发）</summary>
    public List<HotkeyBinding> Hotkeys { get; set; } =
    [
        new() { Action = HotkeyAction.ToggleWindow, Modifier = "Alt", Key = "Q" },
        new() { Action = HotkeyAction.ToggleSidebar, Modifier = "Alt", Key = "E" },
        new() { Action = HotkeyAction.ToggleSize, Modifier = "Alt", Key = "W" },
        new() { Action = HotkeyAction.ToggleSettings, Modifier = "Alt", Key = "S" },
        new() { Action = HotkeyAction.PrevPlatform, Modifier = "Alt", Key = "Left" },
        new() { Action = HotkeyAction.NextPlatform, Modifier = "Alt", Key = "Right" },
    ];

    /// <summary>窗口尺寸与位置记忆</summary>
    public WindowSettings Window { get; set; } = new();

    /// <summary>运行行为设置</summary>
    public BehaviorSettings Behavior { get; set; } = new();

    /// <summary>外观主题：Light / Dark</summary>
    public string Theme { get; set; } = "Light";

    /// <summary>构造函数：不填充任何默认热键，全部由用户自行设置</summary>
    public AppSettings() { }
}

/// <summary>单条热键绑定</summary>
public class HotkeyBinding
{
    /// <summary>触发后执行的动作</summary>
    public HotkeyAction Action { get; set; } = HotkeyAction.ToggleWindow;

    /// <summary>修饰键组合，如 "Ctrl+Alt"</summary>
    public string Modifier { get; set; } = "";

    /// <summary>触发按键，如 "Space"、"A"、"F1"</summary>
    public string Key { get; set; } = "";
}

/// <summary>热键动作类型：窗口呼出/隐藏、侧边栏折叠、平台前后切换</summary>
public enum HotkeyAction
{
    /// <summary>显示/隐藏主窗口</summary>
    ToggleWindow,

    /// <summary>展开/折叠侧边栏</summary>
    ToggleSidebar,

    /// <summary>切换小窗/大窗尺寸</summary>
    ToggleSize,

    /// <summary>打开/关闭设置（操作）窗口</summary>
    ToggleSettings,

    /// <summary>切换到上一个平台（循环，首项跳到末项）</summary>
    PrevPlatform,

    /// <summary>切换到下一个平台（循环，末项跳到首项）</summary>
    NextPlatform,
}

/// <summary>窗口尺寸与位置记忆</summary>
public class WindowSettings
{
    /// <summary>小窗模式宽</summary>
    public double CompactWidth { get; set; } = 630;

    /// <summary>小窗模式高</summary>
    public double CompactHeight { get; set; } = 780;

    /// <summary>大窗模式宽</summary>
    public double FullWidth { get; set; } = 1180;

    /// <summary>大窗模式高</summary>
    public double FullHeight { get; set; } = 760;

    /// <summary>是否记住上次窗口位置</summary>
    public bool RememberPosition { get; set; } = true;

    /// <summary>上次窗口 Left</summary>
    public double? Left { get; set; }

    /// <summary>上次窗口 Top</summary>
    public double? Top { get; set; }

    /// <summary>上次是否为小窗模式（默认小窗）</summary>
    public bool IsCompact { get; set; } = true;

    /// <summary>侧边栏是否折叠（独立记忆，不随窗口大小变化；默认折叠）</summary>
    public bool SidebarCollapsed { get; set; } = true;
}

/// <summary>运行行为设置</summary>
public class BehaviorSettings
{
    /// <summary>启动时是否隐藏到托盘（默认 false：启动即显示主窗口）</summary>
    public bool StartHidden { get; set; } = false;

    /// <summary>默认选中平台 Id</summary>
    public string DefaultPlatformId { get; set; } = "deepseek";
}