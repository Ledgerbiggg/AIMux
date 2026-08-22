using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AiMux.Common.Config;
using AiMux.Common.Logger;
using AiMux.Models;
using AiMux.Services.IService;

namespace AiMux.Services.Service;

/// <summary>平台图标服务实现：favicon 抓取（统一转 png 缓存）、本地图片拷贝、兜底色哈希</summary>
public class IconService : IIconService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(12),
    };

    private readonly ConfigService _config;

    static IconService() => Http.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");

    public IconService(ConfigService config) => _config = config;

    /// <summary>返回图标路径：http(s) 图片链接直接返回（主界面可直接加载远程图标）；
    /// 本地路径需文件存在才返回，否则返回空串</summary>
    public string GetIconPath(PlatformInfo platform)
    {
        if (string.IsNullOrEmpty(platform.Icon))
            return "";
        // 图片链接（http/https）无需本地文件，直接返回供主界面加载
        if (platform.Icon.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            platform.Icon.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return platform.Icon;
        if (File.Exists(platform.Icon))
            return platform.Icon;
        return "";
    }

    /// <summary>自动抓取 favicon：优先 /favicon.ico，失败解析 HTML 的 link rel=icon</summary>
    public async Task<string> AutoFetchAsync(PlatformInfo platform)
    {
        try
        {
            var iconUrl = await FindFaviconUrlAsync(platform.Url);
            if (string.IsNullOrEmpty(iconUrl))
                return "";

            var bytes = await Http.GetByteArrayAsync(iconUrl);
            return SaveBytesAsPng(platform, bytes);
        }
        catch (Exception ex)
        {
            LoggerHelper.Info($"获取图标失败 {platform.Url}: {ex.Message}");
            return "";
        }
    }

    /// <summary>查找站点图标地址：先探测 /favicon.ico，再解析 HTML</summary>
    private static async Task<string?> FindFaviconUrlAsync(string url)
    {
        try
        {
            var uri = new Uri(url);
            var favicon = new Uri(uri, "/favicon.ico");
            using var resp = await Http.GetAsync(favicon, HttpCompletionOption.ResponseHeadersRead);
            if (resp.IsSuccessStatusCode && resp.Content.Headers.ContentType?.MediaType != "text/html")
                return favicon.AbsoluteUri;
        }
        catch
        {
            // 探测失败继续解析 HTML
        }

        try
        {
            var html = await Http.GetStringAsync(url);
            // 匹配 <link rel="icon" href="..."> 与属性顺序互换的写法
            var match = Regex.Match(html,
                "<link[^>]+rel=[\"']?(?:shortcut\\s+)?icon[\"']?[^>]+href=[\"']([^\"']+)[\"']",
                RegexOptions.IgnoreCase);
            if (!match.Success)
                match = Regex.Match(html,
                    "<link[^>]+href=[\"']([^\"']+)[\"'][^>]+rel=[\"']?(?:shortcut\\s+)?icon[\"']?",
                    RegexOptions.IgnoreCase);
            return match.Success
                ? new Uri(new Uri(url), match.Groups[1].Value).AbsoluteUri
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把本地图片拷贝到图标缓存（统一转 png）</summary>
    public string SaveLocalCopy(PlatformInfo platform, string sourcePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(sourcePath);
            return SaveBytesAsPng(platform, bytes);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"上传图标失败: {sourcePath}", ex);
            return "";
        }
    }

    /// <summary>字节流统一解码并转 png 保存到 icons\{id}.png：先把帧画到 32x32 透明画布居中，保证图标归一化、不变形不偏位</summary>
    private string SaveBytesAsPng(PlatformInfo platform, byte[] bytes)
    {
        var dest = Path.Combine(_config.IconsDir, $"{platform.Id}.png");
        try
        {
            BitmapFrame frame;
            using (var ms = new MemoryStream(bytes))
            {
                try
                {
                    var decoder = new IconBitmapDecoder(ms,
                        BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    frame = decoder.Frames[0];
                }
                catch
                {
                    ms.Position = 0;
                    var decoder = BitmapDecoder.Create(ms,
                        BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    frame = decoder.Frames[0];
                }
            }

            // 归一化：缩放至 28x28 并居中绘制到 32x32 透明画布，避免 favicon 尺寸/透明边距不一致导致图标偏移或偏小
            const int size = 32;
            const int inner = 28;
            var drawing = new DrawingVisual();
            using (var ctx = drawing.RenderOpen())
            {
                var scale = Math.Min((double)inner / frame.PixelWidth, (double)inner / frame.PixelHeight);
                var w = frame.PixelWidth * scale;
                var h = frame.PixelHeight * scale;
                ctx.DrawImage(frame, new Rect((size - w) / 2, (size - h) / 2, w, h));
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(drawing);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(dest);
            encoder.Save(fs);
            return dest;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"图标解码失败: {dest}", ex);
            return "";
        }
    }

    /// <summary>获取兜底色：已配置直接返回，否则按名称哈希生成稳定颜色</summary>
    public string GetAccentColor(PlatformInfo platform)
    {
        if (!string.IsNullOrWhiteSpace(platform.AccentColor))
            return platform.AccentColor;
        return HashColor(platform.Name);
    }

    /// <summary>FNV-1a 哈希名称，映射为固定 HSL 色相，保证同名同色、异名异色</summary>
    private static string HashColor(string name)
    {
        uint hash = 2166136261;
        foreach (var c in name)
        {
            hash ^= c;
            hash *= 16777619;
        }
        var (r, g, b) = HslToRgb(hash % 360, 0.45, 0.42);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>HSL 转 RGB（h: 0-360, s/l: 0-1），返回 0-255 分量</summary>
    private static (int R, int G, int B) HslToRgb(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r1, g1, b1) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return ((int)Math.Round((r1 + m) * 255), (int)Math.Round((g1 + m) * 255), (int)Math.Round((b1 + m) * 255));
    }
}
