namespace AiMux.Services.IService;

/// <summary>远程最新版本信息（对应仓库根 version.json）</summary>
public class UpdateInfo
{
    /// <summary>最新版本号，如 "0.1.0" 或 "0.2.0-beta"</summary>
    public string Version { get; set; } = "";

    /// <summary>更新说明</summary>
    public string Notes { get; set; } = "";

    /// <summary>下载/发布页地址</summary>
    public string Url { get; set; } = "";
}

/// <summary>版本更新检测服务：拉取远程 version.json 并提供版本比对</summary>
public interface IUpdateService
{
    /// <summary>获取远程最新版本信息；网络异常时返回 null</summary>
    Task<UpdateInfo?> FetchLatestAsync();
}
