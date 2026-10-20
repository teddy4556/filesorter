using YamlDotNet.Serialization;

namespace FileSorter.Core.Models;

/// <summary>
/// 监听模式。实时 = FileSystemWatcher 事件驱动;定时 = 按固定间隔轮询目录。
/// </summary>
public enum WatchMode
{
    /// <summary>实时:文件落地即处理(等稳定期后)。</summary>
    Realtime = 0,

    /// <summary>每 5 分钟轮询一次。</summary>
    Every5Minutes = 1,

    /// <summary>每 30 分钟轮询一次。</summary>
    Every30Minutes = 2,
}

/// <summary>
/// rules.yaml 里的单个监听文件夹配置。
/// </summary>
public class WatchFolder
{
    /// <summary>要监听的文件夹绝对路径。不存在时该条被跳过并记日志。</summary>
    [YamlMember(Alias = "path")]
    public string Path { get; set; } = "";

    /// <summary>关掉这一条而不删配置(临时停用某个目录)。</summary>
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>true = 含子目录递归监听。</summary>
    [YamlMember(Alias = "recursive")]
    public bool Recursive { get; set; }

    /// <summary>实时 还是 定时轮询。YAML 里写 realtime / Every5Minutes / Every30Minutes。</summary>
    [YamlMember(Alias = "mode")]
    public WatchMode Mode { get; set; } = WatchMode.Realtime;

    /// <summary>
    /// 文件大小连续多久不变才认为「写完了」再处理 —— 防半拷贝。
    /// 单位秒。0 = 用默认值 <see cref="FileSorter.Core.WatchFolderService.DefaultSettleSeconds"/>。
    /// 大视频建议 10~30。
    ///
    /// 双 key 兼容:[YamlMember(Alias)] 会**覆盖**属性名映射,所以 snake_case 的
    /// `settle_seconds` 和 camelCase 的 `settleSeconds` 不能共用一个属性 ——
    /// 加了 alias 后 camelCase key 会被静默忽略(探针实测 settle=0)。
    /// 解决:SettleSecondsCamel 作为第二个入口,getter/setter 都转发到 SettleSeconds。
    /// </summary>
    [YamlMember(Alias = "settle_seconds")]
    public int SettleSeconds { get; set; }

    /// <summary>camelCase 入口(`settleSeconds:`),转发到 <see cref="SettleSeconds"/>。</summary>
    [YamlMember(Alias = "settleSeconds")]
    public int SettleSecondsCamel
    {
        get => SettleSeconds;
        set => SettleSeconds = value;
    }

    /// <summary>定时模式下的轮询间隔(秒)。0 = 按 <see cref="Mode"/> 取默认。可单独覆盖。</summary>
    [YamlMember(Alias = "interval_seconds")]
    public int IntervalSeconds { get; set; }

    /// <summary>camelCase 入口(`intervalSeconds:`),转发到 <see cref="IntervalSeconds"/>。</summary>
    [YamlMember(Alias = "intervalSeconds")]
    public int IntervalSecondsCamel
    {
        get => IntervalSeconds;
        set => IntervalSeconds = value;
    }

    /// <summary>解析后的轮询间隔秒数。</summary>
    [YamlIgnore]
    public int EffectiveIntervalSeconds => IntervalSeconds > 0
        ? IntervalSeconds
        : Mode switch
        {
            WatchMode.Every5Minutes => 300,
            WatchMode.Every30Minutes => 1800,
            _ => 0
        };

    /// <summary>解析后的稳定期秒数。</summary>
    [YamlIgnore]
    public int EffectiveSettleSeconds =>
        SettleSeconds > 0 ? SettleSeconds : FileSorter.Core.WatchFolderService.DefaultSettleSeconds;
}
