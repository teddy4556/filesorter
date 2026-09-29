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
        // 2026-09-29: EditPaths 保存后跑 Pruner,删除 unused alias
        // (用户删 rule 后,对应 alias 没引用 → Pruner v6 删掉)
        DestinationPruner.Prune(current);
        var yaml = new SerializerBuilder().Build().Serialize(current);
        var tmp = rulesPath + ".tmp";
        File.WriteAllText(tmp, yaml);
        File.Replace(tmp, rulesPath, null);
    }
}
