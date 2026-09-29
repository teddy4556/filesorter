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
                "yyyyMMdd-HHmmss" => @"\d{8}-\d{6}",     // Twitter: 20260928-103542
                "yyyyMMddHHmm" => @"\d{12}",             // 20260928103542 (no separator)
                "yyyy-MM-dd" => @"\d{4}-\d{2}-\d{2}",
                "yyyyMMdd_HHmmss" => @"\d{8}_\d{6}",
                "yyyy" => @"\d{4}",
                "MM" => @"\d{2}",
                "dd" => @"\d{2}",
                "HH" => @"\d{2}",                        // bare hour
                "mm" => @"\d{2}",                        // bare minute
                _ => throw new ArgumentException($"Unsupported date format: {fmt}")
            };
        }

        return typeSpec switch
        {
            "text" or "string" => @"[a-zA-Z]+",
            "int" or "number" => @"\d+",
            "any" => @"[a-zA-Z0-9._@\-]+", // Twitter @username support (Bug: @KunKunshifupo not in default)
            // v2.3: Unicode token types — match Unicode letters (CJK / Cyrillic / accented) + underscore
            // (excludes \p{N} and '-' so it doesn't eat the template's separator/digit tokens)
            // v2.5 Bug #47: Also accept ° (U+00B0) and full-width parens （）U+FF08/FF09 (Twitter display names)
            //   + digits (\p{Nd}) for display names that start with a digit like "0°C"
            // v2.6 Bug #48: Also accept \p{S} (symbols incl emoji ⬛↝) + \p{M} (marks like ˚)
            // + \p{Cs} (surrogate code units) so emoji in supplementary plane (U+1F426 etc.)
            //   match correctly — .NET char class treats surrogate halves separately
            // NO '-' so template's `-` separator is preserved
            "unicode" or "chinese" or "cjk" => @"[\p{L}\p{N}\p{S}\p{M}\p{Cs}\u00B0\uFF08\uFF09_]+",
            // v2.7.4 Bug #55: ADDED `unicode-dash` variant that ALSO accepts '-' so user names
            //   like '路昕-' (trailing dash), 'hello-world' (mid dash) match. Use ONLY when
            //   the platform's filename format makes '-' part of the username token (not a separator),
            //   e.g. 微博#路昕-#5347047768066952#13.jpg (here `#` is the only separator).
            //   Putting `-` into default `unicode` would break templates where '-' IS the separator.
            // v2.7.6 Bug #57 (2026-09-28): ALSO accept U+00B7 (· middle dot), U+2022 (• bullet), U+30FB (・ katakana middle dot)
            //   for weibo/Twitter usernames with dot-separator style display names like '泠然·'.
            // v2.7.10 (2026-09-29) Bug #58: full-width CJK punctuation: 、 。 ? ! , ; : "" '' ~ — –
            // v2.7.11 (2026-09-29) Bug #59: Twitter/Instagram user-display names contain star/dot/heart
            //   ＊ U+FF0A (fullwidth asterisk), ⑅ U+2445 (circled number 6 marker), ♥ U+2665 (heart)
            //   plus the full OtherPunctuation class \p{Po} (covers all decorative PUA-ish symbols).
            //   \p{Sm}/\p{Sc}/\p{Sk} also picked up so currency/emoji-adjacent math chars don't break.
            "unicode-dash" or "chinese-dash" or "cjk-dash" or "chinese_chars-dash" or "text_unicode-dash" => @"[\p{L}\p{N}\p{S}\p{M}\p{Cs}\p{Po}\u00B0\uFF08\uFF09_\-\s().\u00B7\u2022\u30FB\uFF61\u300E\u300F\u3010\u3011\u300A\u300B\u3001\u3002\uFF1F\uFF01\uFF0C\uFF1B\uFF1A\u201C\u201D\u2018\u2019\uFF5E\u2014\u2013\uFF0A\u2665\u2764\u2728\u2726\u2605\u2606\u2731\u2736\u2738\u2734]+",
            _ => throw new ArgumentException($"Unknown token type: {typeSpec}")
        };
    }
}