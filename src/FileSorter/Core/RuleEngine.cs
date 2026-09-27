using FileSorter.Core.Models;
using YamlDotNet.Serialization;

namespace FileSorter.Core;

public static class RuleEngine
{
    // YamlDotNet 15.x has a thread-safety bug with Dictionary + [YamlMember(Alias)].
    // Workaround: build a fresh deserializer per call so state never persists across invocations.
    private static IDeserializer BuildDeserializer() => new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    public static RulesConfig LoadFromString(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            throw new ArgumentException("YAML is empty", nameof(yaml));
        var cfg = BuildDeserializer().Deserialize<RulesConfig>(yaml)
            ?? throw new InvalidDataException("YAML deserialized to null");
        if (cfg.Version == 0) cfg.Version = 1;
        // YamlDotNet 15.x has a bug with Dictionary + [YamlMember(Alias)] — the alias
        // deserializer reuses the same Dictionary instance and corrupts it. Replace with fresh one.
        cfg.Destinations = new Dictionary<string, string>(cfg.Destinations ?? new());
        cfg.Rules ??= new();
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
            // v2: skip inactive rules
            if (!rule.Active) continue;

            switch (rule.Type)
            {
                case "extension":
                    if (rule.Patterns != null && rule.Patterns.Any(p =>
                        string.Equals(p, ext, StringComparison.OrdinalIgnoreCase)))
                        return DestResult(rule, rule.Destination!);
                    break;

                case "filename_keyword":
                    if (rule.Extensions != null && !rule.Extensions.Contains(ext)) break;
                    if (rule.Patterns != null && rule.Patterns.Any(p =>
                        name.Contains(p, rule.CaseSensitive == true
                            ? StringComparison.Ordinal
                            : StringComparison.OrdinalIgnoreCase)))
                        return DestResult(rule, rule.Destination!);
                    break;

                case "combined":
                    if (rule.Extension != null && !rule.Extension.Contains(ext)) break;
                    if (rule.FilenameKeyword != null && rule.FilenameKeyword.Any(p =>
                        name.Contains(p, rule.CaseSensitive == true
                            ? StringComparison.Ordinal
                            : StringComparison.OrdinalIgnoreCase)))
                        return DestResult(rule, rule.Destination!);
                    break;

                case "path_template":
                    if (rule.Extensions != null && !rule.Extensions.Contains(ext)) break;
                    if (rule.FilenamePattern != null && System.Text.RegularExpressions.Regex.IsMatch(name, rule.FilenamePattern))
                        return TemplateResult(cfg, rule, filePath, name);
                    break;

                case "filename_pattern":
                    // v2: starts_with / ends_with edge matching
                    if (rule.Extensions != null && !rule.Extensions.Contains(ext)) break;
                    if (MatchStartsWith(name, rule.StartsWith, rule.CaseSensitive == true))
                        return DestResult(rule, rule.Destination!);
                    if (MatchEndsWith(name, rule.EndsWith, rule.CaseSensitive == true))
                        return DestResult(rule, rule.Destination!);
                    break;

                case "name_template":
                    // v2: auto-regex from {token} placeholder + mapping-driven path
                    if (MatchNameTemplate(cfg, rule, name, ext, out var ntResult))
                        return ntResult;
                    break;

                case "default":
                    return DestResult(rule, rule.Destination!);
            }
        }
        return null;
    }

    private static MatchResult DestResult(Rule rule, string alias)
    {
        return new MatchResult(rule.Name, alias);
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

    private static bool MatchStartsWith(string name, List<string>? patterns, bool caseSensitive)
    {
        if (patterns == null || patterns.Count == 0) return false;
        var cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return patterns.Any(p => name.StartsWith(p, cmp));
    }

    private static bool MatchEndsWith(string name, List<string>? patterns, bool caseSensitive)
    {
        if (patterns == null || patterns.Count == 0) return false;
        var cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return patterns.Any(p => name.EndsWith(p, cmp));
    }

    private static bool MatchNameTemplate(RulesConfig cfg, Rule rule, string fileName, string ext, out MatchResult? result)
    {
        result = null;
        if (rule.Template == null) return false;

        // Filter by extensions first (cheap)
        if (rule.Extensions != null && rule.Extensions.Count > 0)
        {
            if (!rule.Extensions.Contains(ext)) return false;
        }

        // Compile template → regex
        var regex = NameTemplateCompiler.CompileToRegex(rule.Template);
        var m = System.Text.RegularExpressions.Regex.Match(fileName, regex);
        if (!m.Success) return false;

        var captured = new List<string>();
        for (int i = 1; i < m.Groups.Count; i++) captured.Add(m.Groups[i].Value);

        // Resolve destination via mappings
        var baseDest = ResolveAlias(cfg, rule.Destination ?? "");
        var path = NameTemplatePathBuilder.BuildPath(baseDest, rule.Template, rule.Mappings, captured);
        result = new MatchResult(rule.Name, path);
        return true;
    }

    private static string ResolveAlias(RulesConfig cfg, string alias)
    {
        if (cfg.Destinations.TryGetValue(alias, out var v)) return v;
        // Alias not found → return as-is (might be absolute path already)
        return alias;
    }
}