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
        Assert.Equal(@"^([a-zA-Z0-9]+)_@([a-zA-Z0-9]+)_([a-zA-Z0-9]+)$", re);
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
        Assert.Contains("([a-zA-Z0-9]+)", re); // both tokens
    }

    [Fact]
    public void Compile_Template_With_Dots_And_Spaces()
    {
        var re = NameTemplateCompiler.CompileToRegex("{name}.{ext}");
        Assert.Contains(@"\.([a-zA-Z0-9]+)$", re);
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
}