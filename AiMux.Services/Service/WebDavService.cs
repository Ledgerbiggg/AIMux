using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiMux.Common.Config;
using AiMux.Common.Logger;
using AiMux.Models;
using AiMux.Services.IService;

namespace AiMux.Services.Service;

/// <summary>WebDAV 配置同步服务实现：使用标准 WebDAV 协议（PUT/GET/MKCOL/PROPFIND）进行配置上传/下载</summary>
public class WebDavService : IWebDavService
{
    private static readonly HttpClientHandler Handler = new()
    {
        // 允许自签名证书，WebDAV 服务器常用自签证书
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
    };

    private static readonly HttpClient Client = new(Handler)
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private readonly ConfigService _config;
    private readonly AppSettings _settings;

    /// <summary>远程配置文件名（.aimux 打包格式，与本地导出/导入一致）</summary>
    private const string RemoteFileName = "aimux-config.aimux";

    /// <summary>在 WebDAV 服务器上自动创建的应用专属目录名，所有同步文件统一放在此目录下</summary>
    private const string AppFolderName = "AiMux";

    public WebDavService(ConfigService config)
    {
        _config = config;
        _settings = config.LoadSettings();
    }

    /// <summary>从当前设置重新读取 WebDAV 配置（用户在设置界面修改后调用前需刷新）</summary>
    public void RefreshSettings()
    {
        var fresh = _config.LoadSettings();
        _settings.WebDav = fresh.WebDav;
    }

    /// <summary>WebDAV 配置是否已填写（服务器地址非空）</summary>
    private bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.WebDav.ServerUrl);

    /// <summary>构建 Basic 认证头</summary>
    private AuthenticationHeaderValue BuildAuth()
    {
        var raw = $"{_settings.WebDav.Username}:{_settings.WebDav.Password}";
        var bytes = Encoding.UTF8.GetBytes(raw);
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
    }

    /// <summary>用户填写的 WebDAV 根路径（去尾斜杠），作为基础路径</summary>
    private string BaseUrl => _settings.WebDav.ServerUrl.TrimEnd('/');

    /// <summary>应用专属目录 URL = 用户路径 + /AiMux/</summary>
    private string BuildDirUrl() => $"{BaseUrl}/{AppFolderName}/";

    /// <summary>远程文件完整 URL = 用户路径 + /AiMux/aimux-config.aimux</summary>
    private string BuildFileUrl() => $"{BaseUrl}/{AppFolderName}/{RemoteFileName}";

    /// <summary>创建远程应用目录（MKCOL），已存在则忽略（405/409 均视为成功）</summary>
    private async Task<bool> EnsureRemoteDirAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(new HttpMethod("MKCOL"), BuildDirUrl());
            req.Headers.Authorization = BuildAuth();
            req.Headers.UserAgent.ParseAdd("AiMux-WebDavSync");
            using var resp = await Client.SendAsync(req);
            // 201 Created = 成功；405/409 = 目录已存在；均为正常
            return resp.StatusCode == HttpStatusCode.Created
                || resp.StatusCode == HttpStatusCode.MethodNotAllowed
                || resp.StatusCode == HttpStatusCode.Conflict;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("WebDAV MKCOL 创建目录失败", ex);
            return false;
        }
    }

    /// <summary>测试 WebDAV 连接：PROPFIND 用户根路径 → 确保应用子目录存在</summary>
    public async Task<WebDavResult> TestConnectionAsync()
    {
        // 关键：必须刷新配置，否则用的是构造时的旧配置（可能为空）
        RefreshSettings();

        if (!IsConfigured)
            return new WebDavResult { Ok = false, Message = "请先填写 WebDAV 路径" };

        // 1. PROPFIND 用户填写的根路径，确认可连接
        var rootUrl = BaseUrl + "/";
        try
        {
            using var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), rootUrl);
            req.Headers.Authorization = BuildAuth();
            req.Headers.UserAgent.ParseAdd("AiMux-WebDavSync");
            req.Headers.Add("Depth", "0");
            req.Content = new StringContent(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><propfind xmlns=\"DAV:\"><prop><displayname/></prop></propfind>",
                Encoding.UTF8, "application/xml");

            using var resp = await Client.SendAsync(req);
            // 207 Multi-Status = 根路径存在且可访问
            if (resp.StatusCode == HttpStatusCode.MultiStatus)
            {
                // 2. 确保应用子目录 AiMux/ 存在
                var dirExists = await EnsureRemoteDirAsync();
                if (dirExists)
                    return new WebDavResult { Ok = true, Message = $"连接成功，应用目录 /{AppFolderName}/ 已就绪" };
                return new WebDavResult { Ok = true, Message = $"根路径可访问，但自动创建 /{AppFolderName}/ 子目录失败，请检查权限" };
            }

            // 404 = 根路径不存在
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return new WebDavResult { Ok = false, Message = "根路径不存在，请检查 WebDAV 地址是否正确" };

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                return new WebDavResult { Ok = false, Message = "认证失败：用户名或密码错误" };

            // 读取响应体以获取更多错误信息
            var body = await resp.Content.ReadAsStringAsync();
            LoggerHelper.Info($"WebDAV 测试连接: {rootUrl} -> {(int)resp.StatusCode} {resp.StatusCode}, body={body}");
            return new WebDavResult { Ok = false, Message = $"服务器返回 {(int)resp.StatusCode} {resp.StatusCode}" };
        }
        catch (TaskCanceledException)
        {
            return new WebDavResult { Ok = false, Message = "连接超时，请检查网络和服务器地址" };
        }
        catch (HttpRequestException ex)
        {
            LoggerHelper.Error($"WebDAV 测试连接失败: {rootUrl}", ex);
            var detail = ex.InnerException?.Message ?? ex.Message;
            return new WebDavResult { Ok = false, Message = $"连接失败：{detail}" };
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"WebDAV 测试连接失败: {rootUrl}", ex);
            return new WebDavResult { Ok = false, Message = "连接失败：" + ex.Message };
        }
    }

    /// <summary>上传本地配置到 WebDAV：导出 .aimux 包 → 确保远程目录存在 → PUT 上传</summary>
    public async Task<WebDavResult> UploadAsync()
    {
        if (!IsConfigured)
            return new WebDavResult { Ok = false, Message = "请先填写 WebDAV 服务器地址" };

        RefreshSettings();

        var tmpFile = Path.Combine(Path.GetTempPath(), $"aimux-upload-{Guid.NewGuid():N}.aimux");
        try
        {
            // 1. 导出当前配置为临时 .aimux 文件
            _config.ExportConfig(tmpFile);
            var bytes = await File.ReadAllBytesAsync(tmpFile);

            // 2. 确保远程目录存在
            await EnsureRemoteDirAsync();

            // 3. PUT 上传
            using var req = new HttpRequestMessage(HttpMethod.Put, BuildFileUrl());
            req.Headers.Authorization = BuildAuth();
            req.Headers.UserAgent.ParseAdd("AiMux-WebDavSync");
            req.Content = new ByteArrayContent(bytes);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var resp = await Client.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                var msg = resp.StatusCode == HttpStatusCode.Unauthorized
                    ? "认证失败：用户名或密码错误"
                    : $"服务器返回 {(int)resp.StatusCode} {resp.StatusCode}";
                return new WebDavResult { Ok = false, Message = msg };
            }

            // 4. 记录同步时间
            _settings.WebDav.LastSyncTime = DateTime.UtcNow.ToString("o");
            _config.SaveSettings(_settings);

            return new WebDavResult { Ok = true, Message = "配置已成功上传到 WebDAV 服务器" };
        }
        catch (TaskCanceledException)
        {
            return new WebDavResult { Ok = false, Message = "上传超时，请检查网络和文件大小" };
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("WebDAV 上传配置失败", ex);
            return new WebDavResult { Ok = false, Message = "上传失败：" + ex.Message };
        }
        finally
        {
            try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { /* 清理临时文件失败忽略 */ }
        }
    }

    /// <summary>从 WebDAV 拉取远程配置并应用到本地：GET 远程 .aimux 包 → 导入 → 记录同步时间</summary>
    public async Task<WebDavResult> DownloadAsync()
    {
        if (!IsConfigured)
            return new WebDavResult { Ok = false, Message = "请先填写 WebDAV 服务器地址" };

        RefreshSettings();

        var tmpFile = Path.Combine(Path.GetTempPath(), $"aimux-download-{Guid.NewGuid():N}.aimux");
        try
        {
            // 1. GET 远程配置文件
            using var req = new HttpRequestMessage(HttpMethod.Get, BuildFileUrl());
            req.Headers.Authorization = BuildAuth();
            req.Headers.UserAgent.ParseAdd("AiMux-WebDavSync");

            using var resp = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode)
            {
                var msg = resp.StatusCode switch
                {
                    HttpStatusCode.NotFound => "远程配置文件不存在，请先上传配置",
                    HttpStatusCode.Unauthorized => "认证失败：用户名或密码错误",
                    _ => $"服务器返回 {(int)resp.StatusCode} {resp.StatusCode}"
                };
                return new WebDavResult { Ok = false, Message = msg };
            }

            // 2. 下载到临时文件
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var fs = File.Create(tmpFile);
            await src.CopyToAsync(fs);
            await fs.FlushAsync();

            // 3. 校验文件有效性（尝试解析）
            try
            {
                var json = await File.ReadAllTextAsync(tmpFile);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("Settings", out _) ||
                    !doc.RootElement.TryGetProperty("Platforms", out _))
                    return new WebDavResult { Ok = false, Message = "远程配置文件格式不正确或已损坏" };
            }
            catch
            {
                return new WebDavResult { Ok = false, Message = "远程配置文件解析失败，可能已损坏" };
            }

            // 4. 导入配置
            var (ok, importMsg) = _config.ImportConfig(tmpFile);
            if (!ok)
                return new WebDavResult { Ok = false, Message = importMsg };

            // 5. 记录同步时间
            _settings.WebDav.LastSyncTime = DateTime.UtcNow.ToString("o");
            _config.SaveSettings(_settings);

            return new WebDavResult { Ok = true, Message = "远程配置已成功拉取并应用" };
        }
        catch (TaskCanceledException)
        {
            return new WebDavResult { Ok = false, Message = "下载超时，请检查网络" };
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("WebDAV 下载配置失败", ex);
            return new WebDavResult { Ok = false, Message = "下载失败：" + ex.Message };
        }
        finally
        {
            try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { /* 清理临时文件失败忽略 */ }
        }
    }
}
