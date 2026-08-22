using AiMux.Common.Config;
using AiMux.Models;
using AiMux.Services.IService;

namespace AiMux.Services.Service;

/// <summary>平台管理服务实现：读写 platforms.json，缺失时生成内置默认平台</summary>
public class PlatformService : IPlatformService
{
    private readonly ConfigService _config;
    private List<PlatformInfo>? _cache;

    /// <summary>平台数据变更事件</summary>
    public event EventHandler? PlatformsChanged;

    public PlatformService(ConfigService config) => _config = config;

    /// <summary>获取全部平台；首次调用且无配置时写入内置默认平台</summary>
    public List<PlatformInfo> GetAll()
    {
        if (_cache is not null)
            return _cache;

        var platforms = _config.LoadPlatforms();
        if (platforms.Count == 0)
        {
            platforms = CreateDefaultPlatforms();
            Save(platforms);
        }
        _cache = platforms;
        return platforms;
    }

    /// <summary>按 Id 查找平台</summary>
    public PlatformInfo? GetById(string id) =>
        GetAll().FirstOrDefault(p => p.Id == id);

    /// <summary>保存平台列表并通知订阅者刷新</summary>
    public void Save(List<PlatformInfo> platforms)
    {
        _config.SavePlatforms(platforms);
        _cache = platforms;
        PlatformsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>生成唯一平台 Id（时间戳短哈希，避免与既有 Id 冲突）</summary>
    public string GenerateId()
    {
        while (true)
        {
            var id = "p" + Guid.NewGuid().ToString("N")[..8];
            if (GetById(id) is null)
                return id;
        }
    }

    /// <summary>内置默认平台列表（开箱即用，兜底色对齐界面原型）</summary>
    private static List<PlatformInfo> CreateDefaultPlatforms() =>
    [
        new() { Id = "deepseek", Name = "DeepSeek", Url = "https://chat.deepseek.com", AccentColor = "#0b7a3b", Icon = "https://cdn.jsdelivr.net/npm/@lobehub/icons-static-png@latest/light/deepseek-color.png" },
        new() { Id = "yuanbao", Name = "腾讯元宝", Url = "https://yuanbao.tencent.com/chat", AccentColor = "#1356c9" },
        new() { Id = "zhipu", Name = "智谱清言", Url = "https://chatglm.cn", AccentColor = "#7a3b0b" },
        new() { Id = "kimi", Name = "Kimi", Url = "https://kimi.moonshot.cn", AccentColor = "#5b0b7a" },
        new() { Id = "tongyi", Name = "通义千问", Url = "https://tongyi.aliyun.com/qianwen", AccentColor = "#0b7a72", Icon = "https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/qwen.png" },
        new() { Id = "doubao", Name = "豆包", Url = "https://www.doubao.com/chat", AccentColor = "#b8470b" },
        new() { Id = "chatgpt", Name = "ChatGPT", Url = "https://chatgpt.com", AccentColor = "#111111", Badge = "代理", Icon = "https://cdn.jsdelivr.net/gh/selfhst/icons/png/openai.png" },
    ];
}
