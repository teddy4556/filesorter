using System.Text;
using System.Text.RegularExpressions;

namespace FileSorter.Core;

/// <summary>
/// Compiles a name template like "{platform}_@{author}_{date:yyyyMMdd}" into a regex string.
/// Supported tokens:
///   {name}             → any non-separator (default type: any → [a-zA-Z0-9]+)
///   {name:text}        → [a-zA-Z]+
///   {name:string}      → [a-zA-Z]+
///   {name:int}         → [0-9]+
///   {name:number}      → [0-9]+
///   {name:date:fmt}    → \d{...} based on fmt
///   {name:any}         → [a-zA-Z0-9]+
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
        sb.Append('$');
        return sb.ToString();
    }

    private static string TokenPattern(string inner)
    {
        var colonIdx = inner.IndexOf(':');
        string name = colonIdx < 0 ? inner : inner.Substring(0, colonIdx);
        string typeSpec = colonIdx < 0 ? "any" : inner.Substring(colonIdx + 1);

        // Handle date:fmt → name="date", fmt="yyyyMMdd" etc.
        if (typeSpec.StartsWith("date:"))
        {
            var fmt = typeSpec.Substring("date:".Length);
            return fmt switch
            {
                "yyyyMMdd" => @"\d{8}",
                "HHmmss" => @"\d{6}",
                "yyyy-MM-dd" => @"\d{4}-\d{2}-\d{2}",
                "yyyyMMdd_HHmmss" => @"\d{8}_\d{6}",
                _ => throw new ArgumentException($"Unsupported date format: {fmt}")
            };
        }

        return typeSpec switch
        {
            "text" or "string" => @"[a-zA-Z]+",
            "int" or "number" => @"\d+",
            "any" => @"[a-zA-Z0-9]+",
            _ => throw new ArgumentException($"Unknown token type: {typeSpec}")
        };
    }
}