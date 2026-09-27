using FileSorter.Core;
using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

public class RuleModelTests
{
    [Fact]
    public void New_Rule_Has_Active_Default_True()
    {
        var r = new Rule { Name = "x", Type = "extension", Destination = "y" };
        Assert.True(r.Active);
    }

    [Fact]
    public void New_Rule_Has_Null_Mappings_By_Default()
    {
        var r = new Rule { Name = "x", Type = "extension", Destination = "y" };
        Assert.Null(r.Mappings);
    }

    [Fact]
    public void Mapping_Default_Level_Is_One()
    {
        var m = new Mapping { Token = "{a}" };
        Assert.Equal(1, m.Level);
    }

    [Fact]
    public void Rule_With_Mappings_Roundtrips_Yaml()
    {
        var yaml = """
            version: 1
            default_action: move
            destinations:
              images: D:\cat\img
            rules:
              - name: social
                type: name_template
                template: "{platform}_@{author}"
                mappings:
                  - token: "{platform}"
                    level: 1
                  - token: "{author}"
                    level: 2
            """;

        var cfg = RuleEngine.LoadFromString(yaml);
        Assert.Single(cfg.Rules);
        var r = cfg.Rules![0];
        Assert.Equal("social", r.Name);
        Assert.NotNull(r.Mappings);
        Assert.Equal(2, r.Mappings!.Count);
        Assert.Equal("{platform}", r.Mappings[0].Token);
        Assert.Equal(1, r.Mappings[0].Level);
        Assert.Equal("{author}", r.Mappings[1].Token);
        Assert.Equal(2, r.Mappings[1].Level);
    }
}