using System.Text;
using FileSorter.Core.Models;

namespace FileSorter.Core;

/// <summary>
/// Builds a target directory path from a name_template rule's mappings.
/// Path = baseDestination + (level 1 token value) + (level 2 token value) + ...
/// Tokens not in mappings are ignored.
/// Skipped level numbers are tolerated (no empty dir inserted).
/// </summary>
public static class NameTemplatePathBuilder
{
    public static string BuildPath(
        string baseDestination,
        string template,
        List<Mapping>? mappings,
        IReadOnlyList<string> captured)
    {
        if (mappings == null || mappings.Count == 0)
            return baseDestination;

        // Token name in template (e.g. {platform}) → captured value
        var tokenValues = new Dictionary<string, string>();
        var tokenNames = ExtractTokenNames(template);
        for (int i = 0; i < tokenNames.Count && i < captured.Count; i++)
            tokenValues[tokenNames[i]] = captured[i];

        var sb = new StringBuilder(baseDestination);
        var sortedMappings = mappings.OrderBy(m => m.Level).ToList();
        foreach (var m in sortedMappings)
        {
            if (tokenValues.TryGetValue(m.Token, out var v))
            {
                if (string.IsNullOrEmpty(v)) continue;
                sb.Append('\\');
                sb.Append(SanitizeDirName(v));
            }
        }
        return sb.ToString();
    }

    private static List<string> ExtractTokenNames(string template)
    {
        var names = new List<string>();
        var i = 0;
        while (i < template.Length)
        {
            if (template[i] == '{')
            {
                var end = template.IndexOf('}', i + 1);
                if (end < 0) break;
                var inner = template.Substring(i + 1, end - i - 1);
                var colonIdx = inner.IndexOf(':');
                var name = colonIdx < 0 ? inner : inner.Substring(0, colonIdx);
                names.Add("{" + name + "}");
                i = end + 1;
            }
            else i++;
        }
        return names;
    }

    private static string SanitizeDirName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s;
    }
}