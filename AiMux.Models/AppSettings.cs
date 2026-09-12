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
        // 「打开设置」不设热键（永久下线）：设置走主界面按钮，把 Alt+S 让给将来的功能键
        // Alt+←/→ 给「网页后退/前进」——摸鱼看视频时从播放页返回列表全靠它，比切平台常用得多
        new() { Action = HotkeyAction.WebBack, Modifier = "Alt", Key = "Left" },
        new() { Action = HotkeyAction.WebForward, Modifier = "Alt", Key = "Right" },
        // 切换平台因此让位到 Alt+↑/↓
        new() { Action = HotkeyAction.PrevPlatform, Modifier = "Alt", Key = "Up" },
        new() { Action = HotkeyAction.NextPlatform, Modifier = "Alt", Key = "Down" },
        // 进入/退出摸鱼模式刻意不给热键：统一走 Esc（大窗按进入、小窗按退出，同一个键不会记错）。
        // 横竖屏切换必须保留热键——小窗里没有任何按钮，只能靠键盘切
        new() { Action = HotkeyAction.ToggleMiniOrientation, Modifier = "Alt", Key = "R" },
    ];

    /// <summary>界面显示设置（主界面按钮可见性等）</summary>
    public UiSettings Ui { get; set; } = new();

    /// <summary>窗口尺寸与位置记忆</summary>
    public WindowSettings Window { get; set; } = new();

    /// <summary>运行行为设置</summary>
    public BehaviorSettings Behavior { get; set; } = new();

    /// <summary>外观主题：Light / Dark</summary>
    public string Theme { get; set; } = "Light";

    /// <summary>WebDAV 配置同步设置</summary>
    public WebDavSettings WebDav { get; set; } = new();

    /// <summary>构造函数：不填充任何默认热键，全部由用户自行设置</summary>
    public AppSettings() { }
}

/// <summary>界面显示设置：主界面导航条按钮的可见性（外观设置页可配置）。
/// 全部默认显示，用户可取消勾选隐藏不用的按钮，让导航条更清爽</summary>
public class UiSettings
{
    /// <summary>复制链接按钮（📋）</summary>
    public bool ShowCopyUrl { get; set; } = true;

    /// <summary>回到平台主页按钮（🏠）</summary>
    public bool ShowHome { get; set; } = true;

    /// <summary>大窗/小窗切换按钮（↗）</summary>
    public bool ShowCompactToggle { get; set; } = true;

    /// <summary>刷新网页按钮（↻）</summary>
    public bool ShowReload { get; set; } = true;

    /// <summary>主题切换按钮（🌙/☀）</summary>
    public bool ShowThemeToggle { get; set; } = true;

    /// <summary>置顶窗口按钮（📌）</summary>
    public bool ShowPinToggle { get; set; } = true;

    /// <summary>摸鱼模式按钮（🐟）</summary>
    public bool ShowMiniMode { get; set; } = true;
}

/// <summary>WebDAV 配置同步设置：通过 WebDAV 服务器统一管理多设备配置</summary>
public class WebDavSettings
{
    /// <summary>WebDAV 完整路径，直接填到配置文件存放的目录，如 https://dav.example.com/AiMux</summary>
    public string ServerUrl { get; set; } = "";

    /// <summary>用户名（Basic 认证）</summary>
    public string Username { get; set; } = "";

    /// <summary>密码（Basic 认证）</summary>
    public string Password { get; set; } = "";

    /// <summary>是否在保存设置时自动上传到 WebDAV</summary>
    public bool AutoSync { get; set; } = false;

    /// <summary>上次同步时间（UTC ISO 8601），从未同步为空</summary>
    public string LastSyncTime { get; set; } = "";
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

/// <summary>热键动作类型：窗口呼出/隐藏、侧边栏折叠、平台前后切换、网页导航
/// 注意：本枚举以数字形式持久化在 settings.json 中，**只能往后追加，
/// 绝不能删除成员或调整顺序**，否则旧配置里的数字会静默指向错误的动作</summary>
public enum HotkeyAction
{
    /// <summary>显示/隐藏主窗口</summary>
    ToggleWindow = 0,

    /// <summary>展开/折叠侧边栏</summary>
    ToggleSidebar = 1,

    /// <summary>切换小窗/大窗尺寸</summary>
    ToggleSize = 2,

    /// <summary>打开/关闭设置（操作）窗口</summary>
    ToggleSettings = 3,

    /// <summary>切换到上一个平台（循环，首项跳到末项）</summary>
    PrevPlatform = 4,

    /// <summary>切换到下一个平台（循环，末项跳到首项）</summary>
    NextPlatform = 5,

    /// <summary>已停用：摸鱼模式进出改为 Esc 统一切换（同键双态，不占用可配置热键）。
    /// 保留成员只为兼容旧配置里的数字，不再注册也不再出现在设置页</summary>
    ToggleMiniMode = 6,

    /// <summary>摸鱼模式内切换横屏 / 竖屏朝向</summary>
    ToggleMiniOrientation = 7,

    /// <summary>网页后退（浏览器历史记录回退一层，摸鱼看视频返回列表最常用）</summary>
    WebBack = 8,

    /// <summary>网页前进（浏览器历史记录前进一层）</summary>
    WebForward = 9,
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

    // ===== 摸鱼模式（无边框小窗）=====

    /// <summary>摸鱼模式-横屏宽（16:9，看视频用；默认在原 480 基础上缩小约 1/4）</summary>
    public double MiniLandscapeWidth { get; set; } = 360;

    /// <summary>摸鱼模式-横屏高</summary>
    public double MiniLandscapeHeight { get; set; } = 200;

    /// <summary>摸鱼模式-竖屏宽（9:16，看竖版短视频用；默认约为原 320 的 2/3）</summary>
    public double MiniPortraitWidth { get; set; } = 215;

    /// <summary>摸鱼模式-竖屏高</summary>
    public double MiniPortraitHeight { get; set; } = 284;

    /// <summary>摸鱼模式上次朝向：true = 竖屏，false = 横屏</summary>
    public bool MiniIsPortrait { get; set; }

    /// <summary>摸鱼模式小窗左坐标（独立记忆，不污染主窗口位置）</summary>
    public double? MiniLeft { get; set; }

    /// <summary>摸鱼模式小窗上坐标（独立记忆）</summary>
    public double? MiniTop { get; set; }

    /// <summary>摸鱼模式是否置顶（默认置顶：摸鱼时不被其他窗口盖住）</summary>
    public bool MiniTopmost { get; set; } = true;
}

/// <summary>运行行为设置</summary>
public class BehaviorSettings
{
    /// <summary>启动时是否隐藏到托盘（默认 false：启动即显示主窗口）</summary>
    public bool StartHidden { get; set; } = false;

    /// <summary>默认选中平台 Id</summary>
    public string DefaultPlatformId { get; set; } = "deepseek";
}