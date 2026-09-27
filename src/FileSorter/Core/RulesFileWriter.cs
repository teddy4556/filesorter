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
        // Serialize with CamelCase naming so the YAML reads `defaultAction`, `rules`, etc.
        // (Matches existing rules.yaml style produced by the app.)
        var yaml = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build()
            .Serialize(cfg);
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