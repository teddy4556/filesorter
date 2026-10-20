using YamlDotNet.Serialization;

namespace FileSorter.Core.Models;

public class RulesConfig
{
    [YamlMember(Alias = "version")] public int Version { get; set; }
    [YamlMember(Alias = "default_action")] public string DefaultAction { get; set; } = "move";
    [YamlMember(Alias = "conflict_strategy")] public string ConflictStrategy { get; set; } = "rename";
    [YamlMember(Alias = "log_level")] public string LogLevel { get; set; } = "info";
    [YamlMember(Alias = "destinations")] public Dictionary<string, string> Destinations { get; set; } = null!;
    [YamlMember(Alias = "rules")] public List<Rule> Rules { get; set; } = null!;

    /// <summary>
    /// 自动归类监听目录(2026-10-19 新增)。
    /// 同时接受 <c>watch_folders</c>(snake_case)和 <c>watchFolders</c>(camelCase)两种 key,
    /// 因为现有 yaml 由 RulesFileWriter 以 camelCase 序列化,而手写配置习惯用 snake_case。
    /// 两个 key 都会合并,重复的按 path 去重。
    /// </summary>
    [YamlMember(Alias = "watch_folders")] public List<WatchFolder>? WatchFoldersSnake { get; set; }
    [YamlMember(Alias = "watchFolders")] public List<WatchFolder>? WatchFoldersCamel { get; set; }

    /// <summary>
    /// 合并后的生效监听列表(计算属性,不参与序列化)。
    /// [YamlIgnore] 是必须的:否则 RulesFileWriter 序列化时会额外吐一个 watchFolders 段,
    /// 跟下面两个真实字段重复,读写来回就乱了。
    /// </summary>
    [YamlIgnore]
    public List<WatchFolder> WatchFolders
    {
        get
        {
            var all = new List<WatchFolder>();
            if (WatchFoldersSnake != null) all.AddRange(WatchFoldersSnake);
            if (WatchFoldersCamel != null) all.AddRange(WatchFoldersCamel);
            // 去重:同一路径只保留第一条
            return all.GroupBy(w => w.Path ?? "", StringComparer.OrdinalIgnoreCase)
                       .Select(g => g.First())
                       .ToList();
        }
        set { WatchFoldersSnake = value; WatchFoldersCamel = null; }
    }
}
