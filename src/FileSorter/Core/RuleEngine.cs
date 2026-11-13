using System.IO;
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

        // v2.8 Bug #75: `default` 在 UI 里叫「默认(兜底)」,语义上必须最后生效。
        //   旧实现在 foreach 里 `case "default": return ...` 立即返回,
        //   结果 default 规则在列表里的位置变成了硬闸门 —— 排在第 1 位就吃掉后面全部规则,
        //   排在最后则后面新增的规则永远轮不到。
        //   现在:主循环跳过 default 并记住第一条 active 的,扫完再兜底。
        Rule? fallbackRule = null;

        foreach (var rule in cfg.Rules)
        {
            // v2: skip inactive rules
            if (!rule.Active) continue;

            if (string.Equals(rule.Type, "default", StringComparison.Ordinal))
            {
                fallbackRule ??= rule;
                continue;
            }

            switch (rule.Type)
            {
                case "extension":
                    if (rule.Patterns != null && rule.Patterns.Any(p =>
                        string.Equals(p, ext, StringComparison.OrdinalIgnoreCase)))
                        return DestResult(cfg, rule, rule.Destination!);
                    break;

                case "filename_keyword":
                    if (rule.Extensions != null && !rule.Extensions.Contains(ext)) break;
                    if (rule.Patterns != null && rule.Patterns.Any(p =>
                        name.Contains(p, rule.CaseSensitive == true
                            ? StringComparison.Ordinal
                            : StringComparison.OrdinalIgnoreCase)))
                        return DestResult(cfg, rule, rule.Destination!);
                    break;

                case "combined":
                    if (rule.Extension != null && !rule.Extension.Contains(ext)) break;
                    if (rule.FilenameKeyword != null && rule.FilenameKeyword.Any(p =>
                        name.Contains(p, rule.CaseSensitive == true
                            ? StringComparison.Ordinal
                            : StringComparison.OrdinalIgnoreCase)))
                        return DestResult(cfg, rule, rule.Destination!);
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
                        return DestResult(cfg, rule, rule.Destination!);
                    if (MatchEndsWith(name, rule.EndsWith, rule.CaseSensitive == true))
                        return DestResult(cfg, rule, rule.Destination!);
                    break;

                case "name_template":
                    // v2: auto-regex from {token} placeholder + mapping-driven path
                    if (MatchNameTemplate(cfg, rule, name, ext, out var ntResult))
                        return ntResult;
                    break;

            }
        }

        // 非 default 规则一个都没命中 → 才轮到兜底
        if (fallbackRule != null)
            return DestResult(cfg, fallbackRule, fallbackRule.Destination!);

        return null;
    }

    private static MatchResult DestResult(RulesConfig cfg, Rule rule, string alias)
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

        // CRITICAL FIX: stem-only match.
        // The compiled regex anchors ^...$, so we must strip the file extension from the
        // filename before matching. Done unconditionally — even when the template contains
        // a literal "." (e.g. "{title}.{ext}"), we still match against the stem and supply
        // the captured {ext} from `ext` (the fileName's actual extension).
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var rawRegex = NameTemplateCompiler.CompileToRegex(rule.Template);

        // If the template's last group is a literal "." followed by an {ext} token, strip
        // that suffix from the regex (since the stem no longer has the extension) and
        // we'll inject the real extension as the captured value for the {ext} group.
        var regexForStem = StripTrailingExtFromRegex(rawRegex, out bool strippedExtGroup);

        // v2.8 Bug #74: 双扩展名(如 小红书#xxx#…#010d7e#001#2026-10-13.zh.srt)
        //   Path.GetFileNameWithoutExtension 只剥一层,stem 尾部残留 ".zh";
        //   而正则那边 StripTrailingExtFromRegex 已把 .{ext} 整段剥掉 —— 一边剥一边不剥,
        //   stem 尾部多出的 ".zh" 永远匹配不上 \d{4}-\d{2}-\d{2}$。
        //   修法:生成逐层剥尾的 stem 候选,依次尝试,第一个命中即采用。
        //   仅在 strippedExtGroup(模板自带 .{ext})时产生额外候选,单扩展名行为一字不变。
        var stemCandidates = new System.Collections.Generic.List<string> { stem };
        if (strippedExtGroup)
        {
            var deeper = stem;
            for (int k = 0; k < 3; k++)
            {
                var dot = deeper.LastIndexOf('.');
                if (dot <= 0) break;
                deeper = deeper.Substring(0, dot);
                stemCandidates.Add(deeper);
            }
        }

        System.Text.RegularExpressions.Match m = default!;
        foreach (var candidate in stemCandidates)
        {
            m = System.Text.RegularExpressions.Regex.Match(candidate, regexForStem);
            if (m.Success) break;
        }
        if (!m.Success) return false;

        var captured = new List<string>();
        for (int i = 1; i < m.Groups.Count; i++) captured.Add(m.Groups[i].Value);

        // If we stripped the {ext} capture group from the regex, inject the real extension
        // so the downstream NameTemplatePathBuilder sees the same number of captures.
        if (strippedExtGroup)
        {
            captured.Add(ext);
        }

        // Resolve destination via mappings
        var baseDest = ResolveAlias(cfg, rule.Destination ?? "");
        var path = NameTemplatePathBuilder.BuildPath(baseDest, rule.Template, rule.Mappings, captured);
        result = new MatchResult(rule.Name, path);
        return true;
    }

    /// <summary>
        /// If `regex` ends with `\.(<ext-group>)(?:\s\(\d+\))?(?:-\d+(_\d+)*)?$`, remove that trailing
        /// `.{ext}` literal+group from the regex so it can match the stem (which has no extension).
        /// Returns the new regex string and sets `strippedExtGroup=true` if a group was removed.
        ///
        /// Why: a template like "{title}.{ext}" compiles to e.g.
        ///   ^([a-zA-Z0-9._-]+)\.([a-zA-Z0-9._-]+)(?:\s\(\d+\))?(?:-\d+(_\d+)*)?$
        /// After we strip ".jpg" from the filename, the stem has no trailing ".ext", so the
        /// raw regex won't match. We rewrite to drop both the .({ext}) group and the
        /// dedup/rename suffixes:
        ///   ^([a-zA-Z0-9._-]+)(?:\s\(\d+\))?(?:-\d+(_\d+)*)?$
        /// and remember that one group was dropped — MatchNameTemplate then synthesizes the
        /// {ext} capture from the actual fileName extension.
        /// </summary>
        private static string StripTrailingExtFromRegex(string regex, out bool strippedExtGroup)
                {
                    strippedExtGroup = false;
                    // v2.7+ Bug #52: Regex always ends with `(?:-\d+(_\d+)*)?$` (after `<ext>` group close),
                    //   so look for the LAST occurrence of that suffix, not EndsWith.
                    // Pattern: ^captures...\.(<ext>)(?:\s\(\d+\))?(?:-\d+(_\d+)*)?$
                    const string dedupSuffix = @"(?:-\d+(_\d+)*|_\d+(?:_\d+)*)?$";  // v2.8: 同步 Bug #73 的 _N 分支  // v2.8: 同步 Bug #73 的 _N 分支
                    const string renameSuffix = @"(?:\s\(\d+\))?";

                    int idxDedup = regex.LastIndexOf(dedupSuffix, StringComparison.Ordinal);
                    if (idxDedup < 0) return regex;
                    // The portion BEFORE the dedup suffix must end with the rename suffix (or directly with
                    // a close-paren — meaning no rename suffix was emitted because the file didn't have it).
                    var beforeDedup = regex.Substring(0, idxDedup);
                    int idxRename = beforeDedup.LastIndexOf(renameSuffix, StringComparison.Ordinal);
                    int idxLiteralDot;
                    string inner;
                    if (idxRename >= 0 && idxRename + renameSuffix.Length == beforeDedup.Length)
                    {
                        // rename suffix was present
                        inner = beforeDedup.Substring(0, idxRename);
                    }
                    else
                    {
                        // no rename suffix — use the whole beforeDedup as inner
                        inner = beforeDedup;
                    }
                    // Find `\.(` in inner. The trailing token group is `\.[a-zA-Z0-9._-]+)`.
                    idxLiteralDot = inner.LastIndexOf(@"\.(", StringComparison.Ordinal);
                    if (idxLiteralDot < 0) return regex;
                    // Strip from `\.` onwards; reattach ONLY the dedup suffix + anchor (rename suffix was
                    // already gone if it existed in the original).
                    var rebuilt = inner.Substring(0, idxLiteralDot) + dedupSuffix;
                    strippedExtGroup = true;
                    return rebuilt;
                }

    private static string ResolveAlias(RulesConfig cfg, string alias)
    {
        if (cfg.Destinations.TryGetValue(alias, out var v)) return v;
        // Alias not found → return as-is (might be absolute path already)
        return alias;
    }
}