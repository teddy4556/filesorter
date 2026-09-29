using FileSorter.Core;
using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

public class NameTemplateCompilerTests
{
    [Fact]
    public void Compile_Token_Without_Type_Matches_Any_NonSep()
    {
        var re = NameTemplateCompiler.CompileToRegex("{platform}_@{author}_{title}");
        // compiler 现在在 $ 前允许 optional Windows rename 后缀 (?:\\s\\(\\d+\\))?$ OR Explorer dedup 后缀 (?:-\\d+(_\\d+)*)?$
        Assert.EndsWith(@"(?:\s\(\d+\))?(?:-\d+(_\d+)*)?$", re);
    }

    [Fact]
    public void Compile_Text_Type_Lowercase_Only()
    {
        var re = NameTemplateCompiler.CompileToRegex("{platform:text}_{author}");
        Assert.Contains("[a-zA-Z]+", re);
    }

    [Fact]
    public void Compile_Number_Type_Matches_Digits()
    {
        var re = NameTemplateCompiler.CompileToRegex("img_{idx:number}.jpg");
        Assert.Contains(@"(\d+)", re);
    }

    [Fact]
    public void Compile_Date_Format_yyyyMMdd()
    {
        var re = NameTemplateCompiler.CompileToRegex("snap_{date:yyyyMMdd}.jpg");
        Assert.Contains(@"(\d{8})", re);
    }

    [Fact]
    public void Compile_Date_Format_yyyy_MM_dd()
    {
        var re = NameTemplateCompiler.CompileToRegex("snap_{date:yyyy-MM-dd}.jpg");
        Assert.Contains(@"(\d{4}-\d{2}-\d{2})", re);
    }

    [Fact]
    public void Compile_Literal_At_Stays_Literal()
    {
        var re = NameTemplateCompiler.CompileToRegex("{a}_@{b}");
        Assert.Contains(@"_@", re);  // literal @
        Assert.Contains("([a-zA-Z0-9._-]+)", re); // both tokens
    }

    [Fact]
    public void Compile_Template_With_Dots_And_Spaces()
    {
        var re = NameTemplateCompiler.CompileToRegex("{name}.{ext}");
        // compiler 现在在 $ 前允许 optional Windows rename 后缀 OR Explorer dedup 后缀
        Assert.Contains(@"\.([a-zA-Z0-9._-]+)(?:\s\(\d+\))?(?:-\d+(_\d+)*)?$", re);
    }
}

public class NameTemplatePathBuilderTests
{
    [Fact]
    public void Build_NoMappings_Returns_DestinationFlat()
    {
        var path = NameTemplatePathBuilder.BuildPath(
            baseDestination: @"D:\cat\img",
            template: "{platform}_@{author}",
            mappings: null,
            captured: new[] { "twitter", "hahaoy8" });
        Assert.Equal(@"D:\cat\img", path);
    }

    [Fact]
    public void Build_OneMapping_Level1_Adds_Subdir()
    {
        var mappings = new List<Mapping>
        {
            new() { Token = "{platform}", Level = 1 }
        };
        var path = NameTemplatePathBuilder.BuildPath(
            baseDestination: @"D:\cat\img",
            template: "{platform}_@{author}",
            mappings: mappings,
            captured: new[] { "twitter", "hahaoy8" });
        Assert.Equal(@"D:\cat\img\twitter", path);
    }

    [Fact]
    public void Build_TwoMappings_Level1And2_Nested_Subdir()
    {
        var mappings = new List<Mapping>
        {
            new() { Token = "{platform}", Level = 1 },
            new() { Token = "{author}", Level = 2 }
        };
        var path = NameTemplatePathBuilder.BuildPath(
            baseDestination: @"D:\cat\img",
            template: "{platform}_@{author}",
            mappings: mappings,
            captured: new[] { "twitter", "hahaoy8" });
        Assert.Equal(@"D:\cat\img\twitter\hahaoy8", path);
    }

    [Fact]
    public void Build_Level3_Supported()
    {
        var mappings = new List<Mapping>
        {
            new() { Token = "{a}", Level = 1 },
            new() { Token = "{b}", Level = 3 }   // skip level 2 (no token mapped)
        };
        var path = NameTemplatePathBuilder.BuildPath(
            baseDestination: @"D:\x",
            template: "{a}_{b}",
            mappings: mappings,
            captured: new[] { "foo", "bar" });
        Assert.Equal(@"D:\x\foo\bar", path);  // level 2 missing → just skip (no empty dir)
    }

    [Fact]
    public void ExtractTokenNames_StripsTypeSpec()
    {
        // Used by Mappings UI to populate the token ComboBox.
        // Mapping Token format = "{name}" (without type spec) — ExtractTokenNames must match.
        var names = NameTemplateCompiler.ExtractTokenNames(
            "{platform}_@{author}_{date:yyyy}_{title}.{ext}");
        Assert.Equal(new[] { "{platform}", "{author}", "{date}", "{title}", "{ext}" }, names);
    }

    [Fact]
    public void ExtractTokenNames_EmptyTemplate_ReturnsEmpty()
    {
        Assert.Empty(NameTemplateCompiler.ExtractTokenNames(""));
        Assert.Empty(NameTemplateCompiler.ExtractTokenNames(null!));
    }

    [Fact]
    public void Compile_Instagram_Template_Produces_Correct_Regex()
    {
        // Instagram 模板:instagram-{username}-{uid:number}-{shortcode}-{first_name}-{date:yyyyMMdd}
        // 注:NameTemplateCompiler 默认 type "any" 现在接受 [a-zA-Z0-9._-]+ (含 . _ -)。
        var re = NameTemplateCompiler.CompileToRegex(
            "instagram-{username}-{uid:number}-{shortcode}-{first_name}-{date:yyyyMMdd}");
        // compiler 现在在 $ 前允许 optional Windows rename 后缀 (?:\\s\\(\\d+\\))?$ OR Explorer dedup 后缀 (?:-\\d+(_\\d+)*)?$
        Assert.Contains(@"(?:\s\(\d+\))?(?:-\d+(_\d+)*)?$", re);
    }

    [Fact]
    public void Compile_Instagram_Template_Matches_Real_FileName()
    {
        var re = NameTemplateCompiler.CompileToRegex(
            "instagram-{username}-{uid:number}-{shortcode}-{first_name}-{date:yyyyMMdd}");
        // 注:compiler 生成的 regex 带 ^...$ 锚点,只匹配 stem;扩展名 .jpg 不在模板里,这里只测 stem。
        // 测试 stem 含 . _ - 验证 any type 扩展。
        var m = System.Text.RegularExpressions.Regex.Match(
            "instagram-john.doe_123-1234567890-CwXYZ12345-abc123-20260927", re);
        Assert.True(m.Success);
        Assert.Equal("john.doe_123", m.Groups[1].Value);                       // username WITH . _
        Assert.Equal("1234567890", m.Groups[2].Value);                        // uid
        Assert.Equal("CwXYZ12345", m.Groups[3].Value);                        // shortcode
        Assert.Equal("abc123", m.Groups[4].Value);                            // first_name
        Assert.Equal("20260927", m.Groups[5].Value);                          // date
    }

    [Fact]
    public void Compile_Template_Accepts_Windows_Rename_Suffix()
    {
        // Windows 自动加 (1)/(2) 等防冲突后缀,template 必须接受
        var re = NameTemplateCompiler.CompileToRegex(
            "instagram-{username}-{uid:number}-{shortcode}-{first_name}-{date:yyyyMMdd}");

        // (1) (2) (3) 都应该 match
        var samples = new[] {
            "instagram-__ongi-6030869291-DdUGbZcn4sO-811748761-20260916 (1)",
            "instagram-__ongi-6030869291-DdUGbZcn4sO-811748761-20260916 (2)",
            "instagram-__ongi-6030869291-DdUGbZcn4sO-811748761-20260916 (123)",
        };
        foreach (var s in samples)
        {
            var m = System.Text.RegularExpressions.Regex.Match(s, re);
            Assert.True(m.Success, $"Should match: {s}");
            Assert.Equal("__ongi", m.Groups[1].Value);
            Assert.Equal("6030869291", m.Groups[2].Value);
            Assert.Equal("DdUGbZcn4sO", m.Groups[3].Value);
            Assert.Equal("811748761", m.Groups[4].Value);
            Assert.Equal("20260916", m.Groups[5].Value);
        }

        // 无 (N) 后缀也必须仍 OK
        var plain = System.Text.RegularExpressions.Regex.Match(
            "instagram-__ongi-6030869291-DdUGbZcn4sO-811748761-20260916", re);
        Assert.True(plain.Success);
        Assert.Equal("20260916", plain.Groups[5].Value);
    }

    [Fact]
    public void Compile_Template_Rejects_Other_Suffix()
    {
        // 只接受 (N),不接受其他后缀
        var re = NameTemplateCompiler.CompileToRegex(
            "instagram-{username}-{uid:number}-{shortcode}-{first_name}-{date:yyyyMMdd}");

        // -copy / .bak / -v2 应该 NOT match
        var bad = new[] {
            "instagram-__ongi-6030869291-DdUGbZcn4sO-811748761-20260916-copy",
            "instagram-__ongi-6030869291-DdUGbZcn4sO-811748761-20260916.bak",
            "instagram-__ongi-6030869291-DdUGbZcn4sO-811748761-20260916-v2",
        };
        foreach (var s in bad)
        {
            var m = System.Text.RegularExpressions.Regex.Match(s, re);
            Assert.False(m.Success, $"Should NOT match: {s}");
        }
    }

    [Fact]
    public void BuildPath_UsernameOnly_InstagramExample()
    {
        // 仅 username 进一级目录
        var mappings = new List<Mapping>
        {
            new() { Token = "{username}", Level = 1 },
        };
        var path = NameTemplatePathBuilder.BuildPath(
            baseDestination: @"D:\默认保存\下载\下载(待整理)\自动分类\DCIM\instagram",
            template: "instagram-{username}-{uid:number}-{shortcode}-{first_name}-{date:yyyyMMdd}",
            mappings: mappings,
            captured: new[] { "nasa", "1234567890", "CwXYZ12345", "abc123", "20260927" });
        Assert.Equal(@"D:\默认保存\下载\下载(待整理)\自动分类\DCIM\instagram\nasa", path);
    }

    // v2.3: Unicode token type — matches CJK / Cyrillic / accented chars
    [Fact]
    public void Compile_Unicode_Type_Matches_CJK()
    {
        var re = NameTemplateCompiler.CompileToRegex("twitter-(@{user_id})-{user_name:unicode}-{date-time}-{status_id}");
        // Should match Chinese user_name like "夕阳无限好"
        Assert.Contains(@"\p{L}", re);
        var match = System.Text.RegularExpressions.Regex.Match(
            "twitter-(@Cuminsides0)-夕阳无限好-20260926-142011-2103852135859593660",
            re);
        Assert.True(match.Success);
        Assert.Equal("Cuminsides0", match.Groups[1].Value);
        Assert.Equal("夕阳无限好", match.Groups[2].Value);
    }

    [Fact]
    public void Compile_Chinese_Alias_Of_Unicode()
    {
        var re = NameTemplateCompiler.CompileToRegex("{user_name:chinese}");
        Assert.Contains(@"\p{L}", re);
    }

    [Fact]
    public void Compile_CJK_Alias_Of_Unicode()
    {
        var re = NameTemplateCompiler.CompileToRegex("{user_name:cjk}");
        Assert.Contains(@"\p{L}", re);
    }

    [Fact]
    public void BuildPath_Unicode_UserName_Twitter()
    {
        // 真实场景:twitter 文件 user_name 含中文
        var mappings = new List<Mapping>
        {
            new() { Token = "{user_id}", Level = 1 },
        };
        var path = NameTemplatePathBuilder.BuildPath(
            baseDestination: @"D:\默认保存\下载\下载(待整理)\自动分类\DCIM\twitter",
            template: "twitter-(@{user_id})-{user_name:unicode}-{date-time}-{status_id}",
            mappings: mappings,
            captured: new[] { "Cuminsides0", "夕阳无限好", "20260926-142011", "2103852135859593660" });
        Assert.Equal(@"D:\默认保存\下载\下载(待整理)\自动分类\DCIM\twitter\Cuminsides0", path);
    }

    // v2.3 Bug #46: support Explorer-style dedup suffix -N_N_N at end
    [Fact]
    public void Compile_Anchor_Accepts_Explorer_Dedup_Suffix()
    {
        var re = NameTemplateCompiler.CompileToRegex("twitter-(@{user_id})-{user_name:unicode}-{date-time}-{status_id}");
        // Should match Explorer-style dedup: -0_1_1_1, -1_1, etc.
        var match = System.Text.RegularExpressions.Regex.Match(
            "twitter-(@Cuminsides0)-夕阳无限好-20260926-142011-2103852135859593660-0_1_1_1",
            re);
        Assert.True(match.Success, $"Should match with -0_1_1_1 suffix");
        Assert.Equal("Cuminsides0", match.Groups[1].Value);
        Assert.Equal("夕阳无限好", match.Groups[2].Value);
    }

    [Fact]
    public void Compile_Anchor_Accepts_Simple_Dedup_Suffix()
    {
        var re = NameTemplateCompiler.CompileToRegex("img-{n:number}");
        Assert.True(System.Text.RegularExpressions.Regex.Match("img-42-1_2_3", re).Success);
        Assert.True(System.Text.RegularExpressions.Regex.Match("img-42", re).Success);
        Assert.True(System.Text.RegularExpressions.Regex.Match("img-42 (1)", re).Success);
    }

    [Fact]
    public void Compile_Anchor_Accepts_Both_Suffixes()
    {
        var re = NameTemplateCompiler.CompileToRegex("img-{n:number}");
        // (1) AND -1_2 should not both be present — either or neither
        Assert.True(System.Text.RegularExpressions.Regex.Match("img-42", re).Success);
        Assert.True(System.Text.RegularExpressions.Regex.Match("img-42 (1)", re).Success);
        Assert.True(System.Text.RegularExpressions.Regex.Match("img-42-1_2", re).Success);
    }

    // v2.5 Bug #47: Unicode type must accept ° and full-width parens for Twitter display names
    [Fact]
    public void Compile_Unicode_Type_Accepts_Degree_Sign()
    {
        var re = NameTemplateCompiler.CompileToRegex("twitter-(@{user_id})-{user_name:unicode}-{date-time}-{status_id}");
        var match = System.Text.RegularExpressions.Regex.Match(
            "twitter-(@Lris888)-0\u00b0C-20260927-033826-2104053021021794563_1_1_1_1_1_1_1",
            re);
        Assert.True(match.Success, $"Should match 0°C user_name");
        Assert.Equal("Lris888", match.Groups[1].Value);
        Assert.Equal("0\u00b0C", match.Groups[2].Value);
    }

    [Fact]
    public void Compile_Unicode_Type_Accepts_FullWidth_Parens()
    {
        var re = NameTemplateCompiler.CompileToRegex("twitter-(@{user_id})-{user_name:unicode}-{date-time}-{status_id}");
        var match = System.Text.RegularExpressions.Regex.Match(
            "twitter-(@lhvin8462)-Lhvin阿翔（惠州）-20260927-032435-2104049535052058952_1_1_1_1_1",
            re);
        Assert.True(match.Success, $"Should match Lhvin阿翔（惠州） user_name");
        Assert.Equal("lhvin8462", match.Groups[1].Value);
        Assert.Equal("Lhvin阿翔（惠州）", match.Groups[2].Value);
    }

    // v2.6 Bug #48: Unicode type must accept emoji, symbols, and marks for Twitter display names
    [Fact]
    public void Compile_Unicode_Type_Accepts_Emoji_And_Symbols()
    {
        var re = NameTemplateCompiler.CompileToRegex("twitter-(@{user_id})-{user_name:unicode}-{date-time}-{status_id}");
        var match = System.Text.RegularExpressions.Regex.Match(
            "twitter-(@Lris_Cc)-\uD83D\uDC26\u2B1B\u94ED\u9723\u02DA\u219D-20260927-035155-2104056415430005210",
            re);
        Assert.True(match.Success, $"Should match 🐦⬛银霣˚↝ user_name with emoji and symbols");
        Assert.Equal("Lris_Cc", match.Groups[1].Value);
        Assert.Equal("\uD83D\uDC26\u2B1B\u94ED\u9723\u02DA\u219D", match.Groups[2].Value);
    }

    [Fact]
    public void Compile_Unicode_Type_Accepts_Pig_Emoji()
    {
        var re = NameTemplateCompiler.CompileToRegex("twitter-(@{user_id})-{user_name:unicode}-{date-time}-{status_id}");
        var match = System.Text.RegularExpressions.Regex.Match(
            "twitter-(@PIKAQ_Q)-PIKA\uD83D\uDC37-20260925-111516-2103443213822710080",
            re);
        Assert.True(match.Success, $"Should match PIKA🐷 user_name");
        Assert.Equal("PIKAQ_Q", match.Groups[1].Value);
        Assert.Equal("PIKA\uD83D\uDC37", match.Groups[2].Value);
    }

    [Fact]
    public void Twitter_Template_Real_File_Diagnostic()
    {
        var re = NameTemplateCompiler.CompileToRegex("twitter#(@{user_id})#{user_name:unicode-dash}#{date-time}#{status_id:number}");
        var stem = "twitter#(@KunKunshifupo)#困困 （10.6-13广📸）#20260928-103542#2104520418740883770";
        var m = System.Text.RegularExpressions.Regex.Match(stem, re);
        // Diagnostic: dump regex + match state
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Regex: " + re);
        sb.AppendLine("Stem: " + stem);
        sb.AppendLine("Match.Success: " + m.Success);
        if (m.Success) {
            sb.AppendLine("Groups:");
            for (int i = 1; i < m.Groups.Count; i++) {
                sb.AppendLine("  [" + i + "]: `" + m.Groups[i].Value + "`");
            }
        }
        // Twitter @username + dot-in-display-name works (Bug v2.7.7)
        Assert.True(m.Success, "Expected match but got False");
        Assert.Equal("KunKunshifupo", m.Groups[1].Value);
        Assert.Equal("困困 （10.6-13广📸）", m.Groups[2].Value);
        Assert.Equal("20260928-103542", m.Groups[3].Value);
        Assert.Equal("2104520418740883770", m.Groups[4].Value);
    }





    [Fact]
    public void Twitter_Oemixn_Kaomoji_FullWidth_Dot_Match()
    {
        // Twitter user_name 含 halfwidth katakana ｡ U+FF61 (Po),不在 \p{S} 里,需显式加
        var re = NameTemplateCompiler.CompileToRegex("twitter#(@{user_id})#{user_name:unicode-dash}#{date-time}#{status_id:number}");
        var stem = "twitter#(@oemixn)#(｡•̀ᴗ-)✧#20260928-133210#2104564828878831716";
        var m = System.Text.RegularExpressions.Regex.Match(stem, re);
        Assert.True(m.Success, "Twitter kaomoji user_name should match (｡ U+FF61 added)");
        Assert.Equal("oemixn", m.Groups[1].Value);
        Assert.Equal("(｡•̀ᴗ-)✧", m.Groups[2].Value);
        Assert.Equal("20260928-133210", m.Groups[3].Value);
        Assert.Equal("2104564828878831716", m.Groups[4].Value);
    }


    [Fact]
    public void Twitter_SexBrides_CornerBrackets_JpTitle_Match()
    {
        // user_name 含 『』 U+300E/F (Ps/Pe corner brackets,日文/中文标题书名号)
        var re = NameTemplateCompiler.CompileToRegex("twitter#(@{user_id})#{user_name:unicode-dash}#{date-time}#{status_id:number}");
        var stem = "twitter#(@SexBrides)#『酥•妻』少妇 人妻 新娘#20260928-144424#2104583006518444205-3";
        var m = System.Text.RegularExpressions.Regex.Match(stem, re);
        Assert.True(m.Success, "Twitter SexBrides 文件应该 match (『』U+300E/F 已加)");
        Assert.Equal("SexBrides", m.Groups[1].Value);
        Assert.Equal("『酥•妻』少妇 人妻 新娘", m.Groups[2].Value);
        Assert.Equal("20260928-144424", m.Groups[3].Value);
        Assert.Equal("2104583006518444205", m.Groups[4].Value);
    }

}