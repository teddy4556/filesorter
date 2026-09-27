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
        Assert.Equal(@"^([a-zA-Z0-9._-]+)_@([a-zA-Z0-9._-]+)_([a-zA-Z0-9._-]+)$", re);
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
        Assert.Contains(@"\.([a-zA-Z0-9._-]+)$", re);
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
        Assert.Contains(@"^instagram-([a-zA-Z0-9._-]+)-(\d+)-([a-zA-Z0-9._-]+)-([a-zA-Z0-9._-]+)-(\d{8})$", re);
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
    public void Build_Instagram_Path_With_Username_Mapping()
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
}