using System.Text.Json.Serialization;

namespace AiMux.Models;

/// <summary>AI 聊天平台配置项（对应 platforms.json 中的一条记录）</summary>
public class PlatformInfo
{
    /// <summary>唯一标识，切换与持久化使用</summary>
    public string Id { get; set; } = "";

    /// <summary>平台显示名称，如 DeepSeek</summary>
    public string Name { get; set; } = "";

    /// <summary>网页版地址</summary>
    public string Url { get; set; } = "";

    /// <summary>图标路径（本地缓存文件路径或网络 URL），空则用首字母兜底</summary>
    public string Icon { get; set; } = "";

    /// <summary>自动聚焦输入框的 CSS 选择器，留空走通用兜底策略</summary>
    public string FocusSelector { get; set; } = "";

    /// <summary>是否在侧边栏显示</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>图标兜底色 #RRGGBB（favicon 加载失败或缺失时的背景色）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string AccentColor { get; set; } = "";

    /// <summary>可选角标标签，如 ChatGPT 的"代理"提示</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string Badge { get; set; } = "";
}
