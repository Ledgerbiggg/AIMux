using AiMux.Models;

namespace AiMux.Services.IService;

/// <summary>平台图标服务：favicon 自动抓取、手动上传拷贝、兜底色生成</summary>
public interface IIconService
{
    /// <summary>返回平台图标本地缓存路径；无缓存返回空串</summary>
    string GetIconPath(PlatformInfo platform);

    /// <summary>自动抓取网站 favicon 并缓存到本地，失败返回空串</summary>
    Task<string> AutoFetchAsync(PlatformInfo platform);

    /// <summary>把本地图片拷贝/转换到图标缓存，返回新图标路径</summary>
    string SaveLocalCopy(PlatformInfo platform, string sourcePath);

    /// <summary>获取图标兜底色：已配置直接返回，否则按名称哈希生成稳定色</summary>
    string GetAccentColor(PlatformInfo platform);
}
