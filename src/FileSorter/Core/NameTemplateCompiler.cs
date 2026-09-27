using System.Text;
using System.Text.RegularExpressions;

namespace FileSorter.Core;

/// <summary>
/// Compiles a name template like "{platform}_@{author}_{date:yyyyMMdd}" into a regex string.
/// Supported tokens:
///   {name}             → any non-separator (default type: any → [a-zA-Z0-9._-]+)
///   {name:text}        → [a-zA-Z]+
///   {name:string}      → [a-zA-Z]+
///   {name:int}         → [0-9]+
///   {name:number}      → [0-9]+
///   {name:date:fmt}    → \d{...} based on fmt
///   {name:any}         → [a-zA-Z0-9._-]+
/// Literal characters are escaped for regex.
/// </summary>
public static class NameTemplateCompiler
{
    public static string CompileToRegex(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
            throw new ArgumentException("Template is empty", nameof(template));

        var sb = new StringBuilder();
        sb.Append('^');
        var i = 0;
        while (i < template.Length)
        {
            if (template[i] == '{')
            {
                var end = template.IndexOf('}', i + 1);
                if (end < 0) throw new ArgumentException($"Unclosed token at index {i}", nameof(template));
                var inner = template.Substring(i + 1, end - i - 1);  // e.g. "platform" or "date:yyyyMMdd"
                sb.Append('(');
                sb.Append(TokenPattern(inner));
                sb.Append(')');
                i = end + 1;
            }
            else
            {
                sb.Append(Regex.Escape(template[i].ToString()));
                i++;
            }
        }
        // v2.3 anchor: 支持 (N) Windows rename 后缀 OR Explorer-style -N_N_N dedup 后缀
        sb.Append(@"(?:\s\(\d+\))?(?:-\d+(_\d+)*)?$");
        return sb.ToString();
    }

    /// <summary>
    /// Extracts token names from a template, returning the "{name}" form (without type spec).
    /// Example: "{platform}_@{author}_{date:yyyy}_{title}.{ext}"
    ///   → ["{platform}", "{author}", "{date}", "{title}", "{ext}"]
    /// Used by Mappings UI to populate the token picker ComboBox.
    /// </summary>
    public static List<string> ExtractTokenNames(string template)
    {
        var names = new List<string>();
        if (string.IsNullOrEmpty(template)) return names;
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

    private static string TokenPattern(string inner)
    {
        var colonIdx = inner.IndexOf(':');
        string name = colonIdx < 0 ? inner : inner.Substring(0, colonIdx);
        string typeSpec = colonIdx < 0 ? "any" : inner.Substring(colonIdx + 1);

        // Handle date format: {date:yyyyMMdd} or {name:date:yyyyMMdd}
        // - {date:yyyyMMdd}        → name="date",  typeSpec="yyyyMMdd"  (no colon for date token itself)
        // - {name:date:yyyyMMdd}   → name="name",  typeSpec="date:yyyyMMdd"
        if (name == "date" || typeSpec.StartsWith("date:"))
        {
            var fmt = name == "date" ? typeSpec : typeSpec.Substring("date:".Length);
            return fmt switch
            {
                "yyyyMMdd" => @"\d{8}",
                "HHmmss" => @"\d{6}",
                "yyyy-MM-dd" => @"\d{4}-\d{2}-\d{2}",
                "yyyyMMdd_HHmmss" => @"\d{8}_\d{6}",
                "yyyy" => @"\d{4}",
                "MM" => @"\d{2}",
                "dd" => @"\d{2}",
                _ => throw new ArgumentException($"Unsupported date format: {fmt}")
            };
        }

        return typeSpec switch
        {
            "text" or "string" => @"[a-zA-Z]+",
            "int" or "number" => @"\d+",
            "any" => @"[a-zA-Z0-9._-]+",
            // v2.3: Unicode token types — match Unicode letters (CJK / Cyrillic / accented) + underscore
            // (excludes \p{N} and '-' so it doesn't eat the template's separator/digit tokens)
            "unicode" or "chinese" or "cjk" => @"[\p{L}_]+",
            _ => throw new ArgumentException($"Unknown token type: {typeSpec}")
        };
    }
}