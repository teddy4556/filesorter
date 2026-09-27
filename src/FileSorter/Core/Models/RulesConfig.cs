using YamlDotNet.Serialization;

namespace FileSorter.Core.Models;

public class RulesConfig
{
    [YamlMember(Alias = "version")] public int Version { get; set; }
    [YamlMember(Alias = "default_action")] public string DefaultAction { get; set; } = "move";
    [YamlMember(Alias = "conflict_strategy")] public string ConflictStrategy { get; set; } = "rename";
    [YamlMember(Alias = "log_level")] public string LogLevel { get; set; } = "info";
    [YamlMember(Alias = "destinations")] public Dictionary<string, string> Destinations { get; set; } = new();
    [YamlMember(Alias = "rules")] public List<Rule> Rules { get; set; } = new();
}