namespace AiMux.Services.IService;

/// <summary>WebDAV 配置同步操作结果</summary>
public class WebDavResult
{
    /// <summary>是否成功</summary>
    public bool Ok { get; set; }

    /// <summary>结果消息（成功时为提示，失败时为错误信息）</summary>
    public string Message { get; set; } = "";
}

/// <summary>WebDAV 配置同步服务：通过 WebDAV 协议上传/下载配置文件，实现多设备配置统一</summary>
public interface IWebDavService
{
    /// <summary>测试 WebDAV 连接是否可用（PROPFIND 远程目录）</summary>
    Task<WebDavResult> TestConnectionAsync();

    /// <summary>上传本地配置到 WebDAV（导出 .aimux 包后 PUT 到远程）</summary>
    Task<WebDavResult> UploadAsync();

    /// <summary>从 WebDAV 拉取远程配置并应用到本地（GET 远程 .aimux 包后导入）</summary>
    Task<WebDavResult> DownloadAsync();
}
