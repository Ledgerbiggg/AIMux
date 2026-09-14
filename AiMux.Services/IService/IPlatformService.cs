using AiMux.Models;

namespace AiMux.Services.IService;

/// <summary>平台管理服务：平台列表 CRUD 与默认平台生成</summary>
public interface IPlatformService
{
    /// <summary>平台数据变更事件（侧边栏据此刷新）</summary>
    event EventHandler? PlatformsChanged;

    /// <summary>获取全部平台；配置文件缺失时生成内置默认平台并落盘</summary>
    List<PlatformInfo> GetAll();

    /// <summary>按 Id 查找平台，不存在返回 null</summary>
    PlatformInfo? GetById(string id);

    /// <summary>保存平台列表并触发变更事件</summary>
    void Save(List<PlatformInfo> platforms);

    /// <summary>丢弃内存缓存并从磁盘重读平台列表，触发变更事件通知界面刷新。
    /// 供配置导入/云端拉取后使用——ImportConfig 直接写盘绕过了本服务，
    /// 不重载的话侧边栏仍是旧列表（以前只能靠重启进程解决）</summary>
    void ReloadFromDisk();

    /// <summary>生成唯一平台 Id</summary>
    string GenerateId();
}
