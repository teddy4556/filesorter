using YamlDotNet.Serialization;

namespace FileSorter.Core.Models;

public class Rule
{
    [YamlMember(Alias = "name")] public string Name { get; set; } = "";
    [YamlMember(Alias = "type")] public string Type { get; set; } = ""; // extension | filename_keyword | combined | path_template | default
    [YamlMember(Alias = "patterns")] public List<string>? Patterns { get; set; }
    [YamlMember(Alias = "extension")] public List<string>? Extension { get; set; }
    [YamlMember(Alias = "filename_keyword")] public List<string>? FilenameKeyword { get; set; }
    [YamlMember(Alias = "extensions")] public List<string>? Extensions { get; set; }
    [YamlMember(Alias = "filename_pattern")] public string? FilenamePattern { get; set; }
    [YamlMember(Alias = "path")] public string? Path { get; set; }
    [YamlMember(Alias = "destination")] public string? Destination { get; set; }
    [YamlMember(Alias = "destination_alias")] public string? DestinationAlias { get; set; }
    [YamlMember(Alias = "case_sensitive")] public bool? CaseSensitive { get; set; }
}