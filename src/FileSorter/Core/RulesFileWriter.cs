using FileSorter.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FileSorter.Core;

/// <summary>
/// Atomically writes a RulesConfig to disk with backup rotation.
/// Backup policy: keep newest 3 backups named <c>{path}.bak.YYYYMMDD-HHMMSS</c>.
/// </summary>
public static class RulesFileWriter
{
    private const int KeepBackups = 3;

    /// <summary>
    /// Write cfg to path. If path already exists, a timestamped copy is taken first.
    /// Then write to a .tmp sibling and use File.Replace for an atomic swap.
    /// After the swap, prune old backups so only the newest <see cref="KeepBackups"/> remain.
    /// </summary>
    public static void WriteWithBackup(string path, RulesConfig cfg)
    {
        // Backup existing file (if any) with timestamp
        if (File.Exists(path))
        {
            var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var bakPath = $"{path}.bak.{ts}";
            File.Copy(path, bakPath, overwrite: true);
        }
        // ⚠️⚠️ 千万别直接 Serialize(cfg)(2026-10-19 实测,踩了两轮才修对)
        //
        // RulesConfig 上有三个相关成员:
        //   WatchFoldersSnake  [YamlMember(Alias="watch_folders")]
        //   WatchFoldersCamel  [YamlMember(Alias="watchFolders")]
        //   WatchFolders       [YamlIgnore] 合并只读属性,setter 会写回 Snake
        //
        // 直接 Serialize(cfg) 会同时吐出 watch_folders 和 watchFolders 两个键,
        // YAML 重复 key 后者覆盖前者 -> 保存成功但重载回来 0 个目录
        // (用户反馈「保存未关闭和生效」)。
        //
        // 在 Serialize 前把别名置 null 也没用 —— setter 早就把值写回 Snake 了,
        // 而且两个别名同时被 null 之后整个 watch 段消失,目录同样丢。
        //
        // 正解:用一个专用 DTO,把唯一的输出键映射成 camelCase 的 watchFolders,
        // 完全绕开 RulesConfig 上的别名属性。
        var dto = new RulesConfigWriteDto
        {
            Version = cfg.Version,
            DefaultAction = cfg.DefaultAction,
            ConflictStrategy = cfg.ConflictStrategy,
            LogLevel = cfg.LogLevel,
            Destinations = cfg.Destinations,
            Rules = cfg.Rules,
            // 走合并后的 WatchFolders,只写一份。Count==0 也照样写出这个键,
            // 保证下次读回来是个空列表而不是 null。
            WatchFolders = (cfg.WatchFolders ?? new List<WatchFolder>())
                .Select(WatchFolderWriteDto.From).ToList()
        };

        var yaml = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build()
            .Serialize(dto);

        var tmpPath = path + ".tmp";
        File.WriteAllText(tmpPath, yaml);
        if (File.Exists(path))
            File.Replace(tmpPath, path, null);
        else
            File.Move(tmpPath, path);

        // Rotate backups: keep only the newest KeepBackups.
        var dir = Path.GetDirectoryName(path)!;
        var namePrefix = Path.GetFileName(path) + ".bak.";
        var baks = Directory.GetFiles(dir, namePrefix + "*")
            .OrderByDescending(f => f)
            .ToList();
        foreach (var old in baks.Skip(KeepBackups))
        {
            try { File.Delete(old); } catch { /* best-effort */ }
        }
    }
}

/// <summary>
/// 写盘专用 DTO —— 字段名 = YAML 输出键。
/// 存在的唯一理由:绕开 <see cref="RulesConfig"/> 上的双别名属性,
/// 保证 watchFolders 在 YAML 里只出现一次。改这个类前先读 RulesConfig 里的注释。
/// </summary>
internal sealed class RulesConfigWriteDto
{
    public int Version { get; set; }
    public string DefaultAction { get; set; } = "move";
    public string ConflictStrategy { get; set; } = "rename";
    public string LogLevel { get; set; } = "info";
    public Dictionary<string, string> Destinations { get; set; } = new();
    public List<Rule> Rules { get; set; } = new();
    public List<WatchFolderWriteDto> WatchFolders { get; set; } = new();
}

/// <summary>
/// 单个监听目录的写盘 DTO。
/// <see cref="WatchFolder"/> 上有 SettleSecondsCamel / IntervalSecondsCamel 两个
/// 双 key 兼容别名属性,直接序列化会把 settle_seconds 和 settleSeconds 各写一遍。
/// 这里只输出 camelCase 一套(与 RulesFileWriter 的 CamelCaseNamingConvention 一致)。
/// </summary>
internal sealed class WatchFolderWriteDto
{
    public string Path { get; set; } = "";
    public bool Enabled { get; set; }
    public bool Recursive { get; set; }
    public WatchMode Mode { get; set; }
    public int SettleSeconds { get; set; }
    public int IntervalSeconds { get; set; }

    public static WatchFolderWriteDto From(WatchFolder w) => new()
    {
        Path = w.Path,
        Enabled = w.Enabled,
        Recursive = w.Recursive,
        Mode = w.Mode,
        // 用 Effective* 而不是裸属性:别名属性被 null 化过时要回落到主属性上
        SettleSeconds = w.SettleSeconds,
        IntervalSeconds = w.IntervalSeconds
    };
}
