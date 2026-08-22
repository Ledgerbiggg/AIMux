using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiMux.Models;

namespace AiMux.Common.Config;

/// <summary>本地 JSON 配置读写服务：管理 %AppData%\AiMux\ 下的配置文件与数据目录</summary>
public class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        // 允许 NaN/Infinity 等特殊浮点值（窗口未记录位置时为 null，此处为防御）
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        // 允许中文直出，便于用户直接编辑配置文件
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>配置根目录（%AppData%\AiMux）</summary>
    public string RootDir { get; }

    /// <summary>平台列表配置文件路径</summary>
    public string PlatformsPath { get; }

    /// <summary>全局设置文件路径</summary>
    public string SettingsPath { get; }

    /// <summary>平台图标缓存目录</summary>
    public string IconsDir { get; }

    /// <summary>WebView2 数据目录（登录态持久化）</summary>
    public string WebViewDataDir { get; }

    /// <summary>日志目录</summary>
    public string LogsDir { get; }

    /// <summary>以默认目录（%AppData%\AiMux）初始化</summary>
    public ConfigService() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AiMux"))
    {
    }

    /// <summary>指定根目录初始化，并确保目录结构存在</summary>
    public ConfigService(string rootDir)
    {
        RootDir = rootDir;
        PlatformsPath = Path.Combine(RootDir, "platforms.json");
        SettingsPath = Path.Combine(RootDir, "settings.json");
        IconsDir = Path.Combine(RootDir, "icons");
        WebViewDataDir = Path.Combine(RootDir, "WebView2Data");
        LogsDir = Path.Combine(RootDir, "logs");
        EnsureDirs();
    }

    /// <summary>确保配置相关目录存在</summary>
    public void EnsureDirs()
    {
        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(IconsDir);
        Directory.CreateDirectory(WebViewDataDir);
        Directory.CreateDirectory(LogsDir);
    }

    /// <summary>读取平台列表；文件缺失或解析失败返回空列表（不抛异常）</summary>
    public List<PlatformInfo> LoadPlatforms() => Load(PlatformsPath, new List<PlatformInfo>());

    /// <summary>保存平台列表到 platforms.json（临时文件替换实现原子写）</summary>
    public void SavePlatforms(List<PlatformInfo> platforms) => Save(PlatformsPath, platforms);

    /// <summary>读取全局设置；文件缺失返回默认设置</summary>
    public AppSettings LoadSettings() => Load(SettingsPath, new AppSettings());

    /// <summary>保存全局设置到 settings.json</summary>
    public void SaveSettings(AppSettings settings)
    {
        Save(SettingsPath, settings);
        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>全局设置变更事件（主窗口据此重新注册热键/刷新状态）</summary>
    public event EventHandler? SettingsSaved;

    /// <summary>导出配置：将 settings.json 与 platforms.json 合并为一个 .aimux 文件，便于备份与迁移</summary>
    public void ExportConfig(string filePath)
    {
        var bundle = new ConfigBundle
        {
            Settings = LoadSettings(),
            Platforms = LoadPlatforms(),
        };
        Save(filePath, bundle);
    }

    /// <summary>导入配置：从 .aimux 文件解析并写回 settings.json / platforms.json。
    /// 成功返回 (true, 提示)；失败返回 (false, 错误信息)。</summary>
    public (bool Ok, string Message) ImportConfig(string filePath)
    {
        try
        {
            var bundle = Load<ConfigBundle>(filePath, default!);
            if (bundle?.Settings is null || bundle.Platforms is null)
                return (false, "文件格式不正确或已损坏");
            SaveSettings(bundle.Settings);
            SavePlatforms(bundle.Platforms);
            return (true, "导入成功");
        }
        catch (Exception ex)
        {
            Logger.LoggerHelper.Error("导入配置失败", ex);
            return (false, "导入失败：" + ex.Message);
        }
    }

    /// <summary>通用读取：文件不存在时返回默认值</summary>
    private static T Load<T>(string path, T fallback) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return fallback;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
        }
        catch (Exception ex)
        {
            Logger.LoggerHelper.Error($"读取配置失败: {path}", ex);
            return fallback;
        }
    }

    /// <summary>通用保存：先写临时文件再替换，避免写入中断损坏配置</summary>
    private static void Save<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, true);
    }

    /// <summary>恢复默认配置：删除平台列表与设置文件，并清空图标缓存目录。
    /// 保留 WebView2 登录态与日志。下次启动会自动重建内置默认平台与默认设置。</summary>
    public void ResetToDefault()
    {
        SafeDelete(PlatformsPath);
        SafeDelete(SettingsPath);
        try
        {
            if (Directory.Exists(IconsDir))
                Directory.Delete(IconsDir, true);
        }
        catch (Exception ex)
        {
            Logger.LoggerHelper.Error("清空图标缓存失败", ex);
        }
        EnsureDirs();
    }

    /// <summary>安全删除文件（不存在或删除失败均忽略）</summary>
    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Logger.LoggerHelper.Error($"删除配置失败: {path}", ex);
        }
    }
}

/// <summary>配置导出/导入的打包结构（合并设置与平台列表）</summary>
public class ConfigBundle
{
    public AppSettings? Settings { get; set; }
    public List<PlatformInfo>? Platforms { get; set; }
}
