using FileSorter.Core.Models;
using YamlDotNet.Serialization;

namespace FileSorter.Core;

public static class RuleEngine
{
    private static readonly IDeserializer _yaml = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    public static RulesConfig LoadFromString(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            throw new ArgumentException("YAML is empty", nameof(yaml));
        var cfg = _yaml.Deserialize<RulesConfig>(yaml)
            ?? throw new InvalidDataException("YAML deserialized to null");
        if (cfg.Version == 0) cfg.Version = 1;
        return cfg;
    }

    public static RulesConfig LoadFromFile(string path)
    {
        return LoadFromString(File.ReadAllText(path));
    }

    public static MatchResult? Match(RulesConfig cfg, string filePath)
    {
        var name = Path.GetFileName(filePath);
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(filePath);

        foreach (var rule in cfg.Rules)
        {
            switch (rule.Type)
            {
                case "extension":
                    if (rule.Patterns != null && rule.Patterns.Any(p =>
                        string.Equals(p, ext, StringComparison.OrdinalIgnoreCase)))
                        return DestResult(cfg, rule, filePath, rule.Destination!);
                    break;

                case "filename_keyword":
                    if (rule.Extensions != null && !rule.Extensions.Contains(ext)) break;
                    if (rule.Patterns != null && rule.Patterns.Any(p =>
                        name.Contains(p, rule.CaseSensitive == true
                            ? StringComparison.Ordinal
                            : StringComparison.OrdinalIgnoreCase)))
                        return DestResult(cfg, rule, filePath, rule.Destination!);
                    break;

                case "combined":
                    if (rule.Extension != null && !rule.Extension.Contains(ext)) break;
                    if (rule.FilenameKeyword != null && rule.FilenameKeyword.Any(p =>
                        name.Contains(p, rule.CaseSensitive == true
                            ? StringComparison.Ordinal
                            : StringComparison.OrdinalIgnoreCase)))
                        return DestResult(cfg, rule, filePath, rule.Destination!);
                    break;

                case "path_template":
                    if (rule.Extensions != null && !rule.Extensions.Contains(ext)) break;
                    if (rule.FilenamePattern != null && System.Text.RegularExpressions.Regex.IsMatch(name, rule.FilenamePattern))
                        return TemplateResult(cfg, rule, filePath, name);
                    break;

                case "default":
                    return DestResult(cfg, rule, filePath, rule.Destination!);
            }
        }
        return null;
    }

    private static MatchResult DestResult(RulesConfig cfg, Rule rule, string filePath, string alias)
    {
        var basePath = ResolveAlias(cfg, alias);
        return new MatchResult(rule.Name, basePath);
    }

    private static MatchResult TemplateResult(RulesConfig cfg, Rule rule, string filePath, string fileName)
    {
        var tpl = rule.Path ?? ResolveAlias(cfg, rule.DestinationAlias ?? "");
        var m = System.Text.RegularExpressions.Regex.Match(fileName, rule.FilenamePattern!);
        var groups = m.Groups;

        string result = tpl;
        // Replace {N} with capture groups (1-based)
        for (int i = 1; i < groups.Count; i++)
            result = result.Replace("{" + i + "}", groups[i].Value);
        // Replace {destinations.<name>}
        foreach (var kv in cfg.Destinations)
            result = result.Replace("{destinations." + kv.Key + "}", kv.Value);
        // Date tokens
        var lastWrite = File.GetLastWriteTime(filePath);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\{date:([^}]+)\}", mm =>
        {
            try { return lastWrite.ToString(mm.Groups[1].Value); }
            catch { return ""; }
        });
        return new MatchResult(rule.Name, result);
    }

    private static string ResolveAlias(RulesConfig cfg, string alias)
    {
        if (cfg.Destinations.TryGetValue(alias, out var v)) return v;
        // Alias not found → return as-is (might be absolute path already)
        return alias;
    }
}