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

    /// <summary>
    /// 把 token 值转成可用的目录名。
    ///
    /// ⚠ 末尾的空格和句点必须归一(2026-11-13 实测,Twitter#𝑮𝒊𝒔𝒕𝒂𝒓𝒂 .#6997340593#...)。
    ///   Windows 建目录时**自动剥掉**名字末尾的空格和点 —— 建出来的是「𝑮𝒊𝒔𝒕𝒂𝒓𝒂」,
    ///   但本方法返回的字符串仍是「𝑮𝒊𝒔𝒕𝒂𝒓𝒂 .」,两者对不上 →
    ///   后面 File.Move 拼出来的路径找不到该目录 → "Could not find a part of the path."
    ///   → 归档静默失败,文件永远躺在「待整理」。
    ///   所以这里必须先归一,让「拼路径用的名字」== 「盘上真实存在的名字」。
    ///
    /// ⚠ 段名整体是 "." 或 ".." 时更危险(2026-11-13 实测,Twitter#..#5908773273#...)。
    ///   "." / ".." 在 Windows 路径里是**目录导航**,不是普通名字:
    ///     作者段 "."  → 路径 DCIM\Twitter\.  → 归一后落回 DCIM\Twitter(平台目录根,作者目录没建)
    ///     作者段 ".." → 路径 DCIM\Twitter\.. → 归一后落进 DCIM\(连平台文件夹都跳出去了!)
    ///   两者都不是「名字」,必须换成普通名字。
    ///   去掉尾点/尾空格后 "." / ".." 会变成空串 → 统一兜底成 "_"。
    ///
    ///   注意:只归一**末尾**;中间的空格/句点是合法字符,必须原样保留。
    /// </summary>
    private static string SanitizeDirName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');

        // 末尾空格/句点归一(Windows 会自动剥掉,留着就对不上盘上真实目录名)
        var trimmed = s.TrimEnd(' ', '.');
        // 全是空格/句点 → 归一后为空 → 兜底成 "_",否则路径会退化成 base+"\"(等于没建作者目录)
        if (trimmed.Length == 0)
            return "_";
        return trimmed;
    }
}