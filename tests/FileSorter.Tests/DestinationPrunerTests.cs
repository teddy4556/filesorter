using FileSorter.Core;
using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

public class DestinationPrunerTests
{
    [Fact]
    public void Prune_Removes_Alias_Not_Referenced_By_Any_Rule()
    {
        var cfg = new RulesConfig
        {
            Destinations = new() { { "images", @"D:\pic" }, { "orphan", @"D:\x" } },
            Rules = new()
        };
        cfg.Rules.Add(new Rule { Name = "r1", Type = "extension", Destination = "images" });

        var result = DestinationPruner.Prune(cfg);

        Assert.Single(result.RemovedKeys);
        Assert.Contains("orphan", result.RemovedKeys);
        Assert.False(cfg.Destinations.ContainsKey("orphan"));
        Assert.True(cfg.Destinations.ContainsKey("images"));
    }

    [Fact]
    public void Prune_Removes_Alias_When_All_Referencing_Rules_Removed()
    {
        // Real-world: user deleted the only rule that used an alias → alias orphaned
        var cfg = new RulesConfig
        {
            Destinations = new() { { "twitter", @"D:\DCIM\twitter" } },
            Rules = new()
        };
        cfg.Rules.Add(new Rule { Name = "twitter_rule", Type = "name_template", Destination = "twitter" });
        cfg.Rules.RemoveAt(0);

        var result = DestinationPruner.Prune(cfg);

        Assert.Single(result.RemovedKeys);
        Assert.Contains("twitter", result.RemovedKeys);
        Assert.False(cfg.Destinations.ContainsKey("twitter"));
    }

    [Fact]
    public void Prune_Keeps_Alias_When_Still_Referenced_After_Rule_Removed()
    {
        var cfg = new RulesConfig
        {
            Destinations = new() { { "twitter", @"D:\DCIM\twitter" } },
            Rules = new()
        };
        cfg.Rules.Add(new Rule { Name = "r1", Type = "name_template", Destination = "twitter" });
        cfg.Rules.Add(new Rule { Name = "r2", Type = "name_template", Destination = "twitter" });
        cfg.Rules.RemoveAt(0);

        var result = DestinationPruner.Prune(cfg);

        Assert.Empty(result.RemovedKeys);
        Assert.Empty(result.NulledRuleNames);
        Assert.True(cfg.Destinations.ContainsKey("twitter"));
    }

    [Fact]
    public void Prune_Keeps_Reserved_Inbox_Even_With_Zero_References()
    {
        var cfg = new RulesConfig
        {
            Destinations = new() { { "inbox", @"D:\收件箱" }, { "orphan", @"D:\x" } },
            Rules = new()
        };

        DestinationPruner.Prune(cfg);

        Assert.True(cfg.Destinations.ContainsKey("inbox"));
        Assert.False(cfg.Destinations.ContainsKey("orphan"));
    }

    [Fact]
    public void Prune_Keeps_Alias_Still_Referenced_By_Another_Rule()
    {
        var cfg = new RulesConfig
        {
            Destinations = new() { { "images", @"D:\pic" } },
            Rules = new()
        };
        cfg.Rules.Add(new Rule { Name = "r1", Type = "extension", Destination = "images" });
        cfg.Rules.Add(new Rule { Name = "r2", Type = "filename_keyword", Destination = "images" });

        DestinationPruner.Prune(cfg);

        Assert.True(cfg.Destinations.ContainsKey("images"));
    }

    [Fact]
    public void Prune_Handles_Null_Rules_And_Destinations_Graciously()
    {
        var cfg = new RulesConfig();
        cfg.Destinations = null!;
        cfg.Rules = null!;

        var result = DestinationPruner.Prune(cfg);

        Assert.NotNull(result);
        Assert.Empty(result.RemovedKeys);
        Assert.Empty(result.NulledRuleNames);
        Assert.Empty(result.RegisteredKeys);
    }

    [Fact]
    public void Prune_Picks_Up_DestinationAlias_As_Reference_Too()
    {
        var cfg = new RulesConfig
        {
            Destinations = new() { { "images", @"D:\pic" } },
            Rules = new()
        };
        cfg.Rules.Add(new Rule { Name = "r1", Type = "extension", DestinationAlias = "images" });

        DestinationPruner.Prune(cfg);

        Assert.True(cfg.Destinations.ContainsKey("images"));
    }

    [Fact]
    public void Prune_Keeps_Alias_When_Rule_Uses_Absolute_Path()
    {
        var cfg = new RulesConfig
        {
            Destinations = new()
            {
                { "instagram", @"D:\Default\DCIM\instagram" },
                { "twitter",   @"D:\Default\DCIM\twitter" }
            },
            Rules = new()
        };
        cfg.Rules.Add(new Rule
        {
            Name = "Instagram",
            Type = "name_template",
            Destination = @"D:\Default\DCIM\instagram"
        });
        cfg.Rules.Add(new Rule
        {
            Name = "Twitter",
            Type = "name_template",
            Destination = @"D:\Default\DCIM\twitter"
        });

        var result = DestinationPruner.Prune(cfg);

        Assert.Empty(result.RemovedKeys);
        Assert.Empty(result.NulledRuleNames);
        Assert.Empty(result.RegisteredKeys);
        Assert.True(cfg.Destinations.ContainsKey("instagram"));
        Assert.True(cfg.Destinations.ContainsKey("twitter"));
    }

    [Fact]
    public void Prune_Removes_Only_Orphan_Aliases_When_Some_Referenced_By_Path()
    {
        var cfg = new RulesConfig
        {
            Destinations = new()
            {
                { "instagram", @"D:\DCIM\instagram" },
                { "archives",  @"D:\zip" },
                { "inbox",     @"D:\inbox" }
            },
            Rules = new()
        };
        cfg.Rules.Add(new Rule
        {
            Name = "InstagramRule",
            Type = "name_template",
            Destination = @"D:\DCIM\instagram"
        });

        var result = DestinationPruner.Prune(cfg);

        Assert.Single(result.RemovedKeys);
        Assert.Contains("archives", result.RemovedKeys);
        Assert.True(cfg.Destinations.ContainsKey("instagram"));
        Assert.True(cfg.Destinations.ContainsKey("inbox"));
        Assert.False(cfg.Destinations.ContainsKey("archives"));
    }

    [Fact]
    public void Prune_Matches_Path_CaseInsensitively_On_Windows()
    {
        var cfg = new RulesConfig
        {
            Destinations = new() { { "twitter", @"D:\DCIM\twitter" } },
            Rules = new()
        };
        cfg.Rules.Add(new Rule
        {
            Name = "r1",
            Type = "name_template",
            Destination = @"d:\dcim\TWITTER"
        });

        var result = DestinationPruner.Prune(cfg);

        Assert.Empty(result.RemovedKeys);
        Assert.True(cfg.Destinations.ContainsKey("twitter"));
    }

    [Fact]
    public void Prune_AutoRegisters_Alias_For_Rule_With_Absolute_Path_Not_In_Dict()
    {
        var cfg = new RulesConfig
        {
            Destinations = new() { { "inbox", @"D:\inbox" } },
            Rules = new()
        };
        cfg.Rules.Add(new Rule
        {
            Name = "weibo",
            Type = "name_template",
            Destination = @"D:\Default\DCIM\weibo"
        });

        var result = DestinationPruner.Prune(cfg);

        Assert.Single(result.RegisteredKeys);
        Assert.Contains("weibo", result.RegisteredKeys);
        Assert.True(cfg.Destinations.ContainsKey("weibo"));
        Assert.Equal(@"D:\Default\DCIM\weibo", cfg.Destinations["weibo"]);
        Assert.Equal("weibo", cfg.Rules[0].Destination);
    }

    [Fact]
    public void Prune_AutoRegisters_Alias_With_Square_Brackets_Preserved()
    {
        var cfg = new RulesConfig
        {
            Destinations = new() { { "inbox", @"D:\inbox" } },
            Rules = new()
        };
        cfg.Rules.Add(new Rule
        {
            Name = "senluo",
            Type = "filename_keyword",
            Destination = @"D:\Default\DCIM\pic\[senluo]"
        });

        var result = DestinationPruner.Prune(cfg);

        Assert.Single(result.RegisteredKeys);
        Assert.Contains("[senluo]", result.RegisteredKeys);
        Assert.Equal("[senluo]", cfg.Rules[0].Destination);
    }

    [Fact]
    public void Prune_AutoRegister_Handles_Collision_With_Suffix()
    {
        var cfg = new RulesConfig
        {
            Destinations = new() { { "inbox", @"D:\inbox" } },
            Rules = new()
        };
        cfg.Rules.Add(new Rule
        {
            Name = "first",
            Type = "name_template",
            Destination = @"D:\a\weibo"
        });
        cfg.Rules.Add(new Rule
        {
            Name = "second",
            Type = "name_template",
            Destination = @"D:\b\weibo"
        });

        var result = DestinationPruner.Prune(cfg);

        Assert.Equal(2, result.RegisteredKeys.Count);
        Assert.Contains("weibo", result.RegisteredKeys);
        Assert.Contains("weibo_2", result.RegisteredKeys);
        Assert.True(cfg.Destinations.ContainsKey("weibo"));
        Assert.True(cfg.Destinations.ContainsKey("weibo_2"));
    }

    [Fact]
    public void Prune_AutoRegister_Suffix_When_Existing_Key_Differs()
    {
        var cfg = new RulesConfig
        {
            Destinations = new()
            {
                { "inbox", @"D:\inbox" },
                { "weibo",  @"D:\old\weibo" }
            },
            Rules = new()
        };
        cfg.Rules.Add(new Rule
        {
            Name = "new",
            Type = "name_template",
            Destination = @"D:\new\weibo"
        });

        var result = DestinationPruner.Prune(cfg);

        Assert.Single(result.RegisteredKeys);
        Assert.Contains("weibo_2", result.RegisteredKeys);
        Assert.Equal(@"D:\old\weibo", cfg.Destinations["weibo"]);
        Assert.Equal(@"D:\new\weibo", cfg.Destinations["weibo_2"]);
    }
}
