using YamlDotNet.Serialization;

namespace FileSorter.Core.Models;

public class Rule
{
    [YamlMember(Alias = "name")] public string Name { get; set; } = "";
    [YamlMember(Alias = "type")] public string Type { get; set; } = ""; // extension | filename_keyword | combined | path_template | default | filename_pattern | name_template
    [YamlMember(Alias = "active")] public bool Active { get; set; } = true;
    [YamlMember(Alias = "mappings")] public List<Mapping>? Mappings { get; set; }
    [YamlMember(Alias = "patterns")] public List<string>? Patterns { get; set; }
    [YamlMember(Alias = "extension")] public List<string>? Extension { get; set; }
    [YamlMember(Alias = "filename_keyword")] public List<string>? FilenameKeyword { get; set; }
    [YamlMember(Alias = "extensions")] public List<string>? Extensions { get; set; }
    [YamlMember(Alias = "filename_pattern")] public string? FilenamePattern { get; set; }
    [YamlMember(Alias = "starts_with")] public List<string>? StartsWith { get; set; }
    [YamlMember(Alias = "ends_with")] public List<string>? EndsWith { get; set; }
    [YamlMember(Alias = "template")] public string? Template { get; set; }
    [YamlMember(Alias = "path")] public string? Path { get; set; }
    [YamlMember(Alias = "destination")] public string? Destination { get; set; }
    [YamlMember(Alias = "destination_alias")] public string? DestinationAlias { get; set; }
    [YamlMember(Alias = "case_sensitive")] public bool? CaseSensitive { get; set; }
}