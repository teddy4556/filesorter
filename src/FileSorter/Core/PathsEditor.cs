using System.IO;
using FileSorter.Core.Models;
using YamlDotNet.Serialization;

namespace FileSorter.Core;

public static class PathsEditor
{
    public static void SavePaths(string rulesPath, Dictionary<string, string> newPaths)
    {
        var current = RuleEngine.LoadFromFile(rulesPath);
        current.Destinations = newPaths;
        var yaml = new SerializerBuilder().Build().Serialize(current);
        var tmp = rulesPath + ".tmp";
        File.WriteAllText(tmp, yaml);
        File.Replace(tmp, rulesPath, null);
    }
}
