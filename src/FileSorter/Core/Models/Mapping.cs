using YamlDotNet.Serialization;

namespace FileSorter.Core.Models;

public class Mapping
{
    [YamlMember(Alias = "token")] public string Token { get; set; } = "";
    [YamlMember(Alias = "level")] public int Level { get; set; } = 1;
}