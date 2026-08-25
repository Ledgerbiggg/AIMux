using System.IO;
using System.Net.Http;
using System.Text.Json;
using AiMux.Common.Logger;
using AiMux.Services.IService;

namespace AiMux.Services.Service;

/// <summary>版本更新检测服务实现：从仓库根 version.json 读取最新版本</summary>
public class UpdateService : IUpdateService
{
    /// <summary>version.json 的 raw 地址（master 分支）。如需国内镜像可改为可访问地址</summary>
    private const string VersionUrl = "https://raw.githubusercontent.com/Ledgerbiggg/AIMux/master/version.json";

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>安装包下载客户端：大文件需要独立长超时（检测用小超时，下载放宽到 15 分钟）</summary>
    private static readonly HttpClient DownloadClient = new() { Timeout = TimeSpan.FromMinutes(15) };

    /// <summary>获取远程最新版本信息；网络异常/解析失败返回 null</summary>
    public async Task<UpdateInfo?> FetchLatestAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, VersionUrl);
            req.Headers.UserAgent.ParseAdd("AiMux-UpdateChecker");
            var resp = await Client.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return null;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new UpdateInfo
            {
                Version = root.GetProperty("version").GetString() ?? "",
                Notes = root.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "",
                Url = root.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 判断远程版本是否比本地新。
    /// 支持 SemVer 预发布后缀（如 -beta）：数字三元组更大 => 更新；
    /// 三元组相等时，本地为预览版、远程为正式版 => 视为更新（正式覆盖预览），
    /// 其余（含本地正式、远程预览）=> 不更新。
    /// </summary>
    public static bool IsNewer(string remoteVersion, string localVersion)
    {
        var r = Parse(remoteVersion);
        var l = Parse(localVersion);

        if (r.Major != l.Major) return r.Major > l.Major;
        if (r.Minor != l.Minor) return r.Minor > l.Minor;
        if (r.Patch != l.Patch) return r.Patch > l.Patch;

        // 三元组相等：正式版(无 pre) 优先于 预览版(有 pre)
        if (string.IsNullOrEmpty(l.Pre) && !string.IsNullOrEmpty(r.Pre)) return false; // 本地正式，远程预览
        if (!string.IsNullOrEmpty(l.Pre) && string.IsNullOrEmpty(r.Pre)) return true;  // 本地预览，远程正式
        return false;
    }

    private static (int Major, int Minor, int Patch, string Pre) Parse(string v)
    {
        if (string.IsNullOrWhiteSpace(v))
            return (0, 0, 0, "");

        var s = v.Trim().TrimStart('v', 'V');
        var pre = "";
        var dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s[(dash + 1)..];
            s = s[..dash];
        }

        var parts = s.Split('.');
        int major = 0, minor = 0, patch = 0;
        if (parts.Length > 0) int.TryParse(parts[0], out major);
        if (parts.Length > 1) int.TryParse(parts[1], out minor);
        if (parts.Length > 2) int.TryParse(parts[2], out patch);
        return (major, minor, patch, pre);
    }

    /// <summary>安装包下载直链：与 CI 产物命名一致（Release tag v{version}，文件名 AiMux-Setup-{version}.exe）</summary>
    private static string BuildInstallerUrl(string version) =>
        $"https://github.com/Ledgerbiggg/AIMux/releases/download/v{version}/AiMux-Setup-{version}.exe";

    /// <summary>下载指定版本安装包到临时目录，按 0-100 报告进度；任何失败返回 null（异常已记日志并清理残留文件）</summary>
    public async Task<string?> DownloadInstallerAsync(string version, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var dest = Path.Combine(Path.GetTempPath(), $"AiMux-Setup-{version}.exe");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, BuildInstallerUrl(version));
            req.Headers.UserAgent.ParseAdd("AiMux-UpdateInstaller");
            using var resp = await DownloadClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            var total = resp.Content.Headers.ContentLength ?? 0L;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var fs = File.Create(dest);
            var buffer = new byte[81920];
            long read = 0;
            while (true)
            {
                var n = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (n <= 0) break;
                await fs.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total > 0)
                    progress?.Report((int)Math.Min(100, read * 100 / total));
            }
            return dest;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"下载安装包失败（{version}）", ex);
            try { File.Delete(dest); } catch { /* 清理残留文件失败忽略 */ }
            return null;
        }
    }
}
