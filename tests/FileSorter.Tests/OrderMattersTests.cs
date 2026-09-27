using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

/// <summary>
/// Regression tests for "rule order matters" — specifically the case where
/// a generic extension rule preempts a more-specific name_template rule.
///
/// Bug repro (2026-09-27): user's rules.yaml has Instagram 图片 last, AFTER
/// the generic `图片 → images` extension rule. Result: instagram-*.jpg files
/// land in `images` instead of per-username instagram/.
/// </summary>
public class OrderMattersTests
{
    private static RulesConfig UserStyleConfig()
    {
        return new RulesConfig
        {
            Destinations = new()
            {
                ["images"]    = @"D:\分类\图片",
                ["documents"] = @"D:\分类\文档",
                ["archives"]  = @"D:\分类\压缩包",
                ["inbox"]     = @"D:\分类\未分类",
                ["instagram"] = @"D:\默认保存\下载\下载（待整理）\自动分类\DCIM\instagram"
            },
            Rules = new()
            {
                new() { Name = "社交媒体图片", Type = "path_template",
                        Extensions = new() { "jpg","jpeg","png","gif","webp" },
                        FilenamePattern = @"^([a-z]+)_\(@([^)]+)\)_",
                        Path = @"{destinations.images}\{1}\@{2}" },
                new() { Name = "截图", Type = "combined",
                        Extension = new() { "png","jpg" },
                        FilenameKeyword = new() { "screenshot", "截图", "screen" },
                        Destination = "images" },
                new() { Name = "发货", Type = "filename_keyword",
                        Patterns = new() { "发货", "invoice", "receipt" },
                        Extensions = new() { "pdf","jpg","png" },
                        Destination = "documents" },
                new() { Name = "压缩包", Type = "extension",
                        Patterns = new() { "zip","rar","7z","tar","gz" },
                        Destination = "archives" },
                new() { Name = "图片", Type = "extension",
                        Patterns = new() { "jpg","jpeg","png","gif","bmp","webp","svg" },
                        Destination = "images" },
                new() { Name = "其他", Type = "default", Destination = "inbox" },
                new() { Name = "Instagram 图片", Type = "name_template",
                        Active = true,
                        Extensions = new() { "jpg","jpeg","png","mp4","webp" },
                        Template = "instagram-{username}-{uid:number}-{shortcode}-{first_name}-{date:yyyyMMdd}",
                        Mappings = new() { new() { Token = "{username}", Level = 1 } },
                        Destination = "instagram" }
            }
        };
    }

    [Fact]
    public void Real_Instagram_File_Gets_Shortcircuited_To_Images_When_Extension_Rule_Preempts()
    {
        var cfg = UserStyleConfig();
        // Real file from user's DCIM\Instagram (captured 2026-09-27 14:37)
        var r = RuleEngine.Match(cfg,
            @"C:\inbox\instagram-__ongi-6030869291-DdUGbZcn4sO-811616677-20260916 (1).jpg");

        // Document the ACTUAL behavior — pre-fix, this returns images dest.
        // If the test suite is green here, the bug is the rule ORDER in
        // user's rules.yaml, NOT the NameTemplate matcher.
        Assert.NotNull(r);
        Assert.Equal("图片", r!.RuleName);
        Assert.Equal(@"D:\分类\图片", r.DestinationPath);
    }

    [Fact]
    public void Instagram_Rule_Fires_When_Listed_Before_Extension_Rule()
    {
        var cfg = UserStyleConfig();
        // Move Instagram rule to first position (the proper fix)
        var ig = cfg.Rules[cfg.Rules.Count - 1];
        cfg.Rules.RemoveAt(cfg.Rules.Count - 1);
        cfg.Rules.Insert(0, ig);

        var r = RuleEngine.Match(cfg,
            @"C:\inbox\instagram-__ongi-6030869291-DdUGbZcn4sO-811616677-20260916 (1).jpg");

        Assert.NotNull(r);
        Assert.Equal("Instagram 图片", r!.RuleName);
        // Per-username path under the instagram destination
        Assert.StartsWith(@"D:\默认保存\下载\下载（待整理）\自动分类\DCIM\instagram\", r.DestinationPath);
    }

    [Fact]
    public void Instagram_Rule_Fires_When_Only_It_Exists()
    {
        // Constructive test: just the Instagram rule + default → instagram
        var cfg = new RulesConfig
        {
            Destinations = new()
            {
                ["images"]    = @"D:\分类\图片",
                ["instagram"] = @"D:\默认保存\下载\下载（待整理）\自动分类\DCIM\instagram"
            },
            Rules = new()
            {
                new() { Name = "其他", Type = "default", Destination = "images" },
                new() { Name = "Instagram 图片", Type = "name_template",
                        Active = true,
                        Extensions = new() { "jpg","jpeg","png","mp4","webp" },
                        Template = "instagram-{username}-{uid:number}-{shortcode}-{first_name}-{date:yyyyMMdd}",
                        Mappings = new() { new() { Token = "{username}", Level = 1 } },
                        Destination = "instagram" }
            }
        };
        var r = RuleEngine.Match(cfg,
            @"C:\inbox\instagram-__ongi-6030869291-DdUGbZcn4sO-811616677-20260916 (1).jpg");

        Assert.NotNull(r);
        Assert.Equal("Instagram 图片", r!.RuleName);
        Assert.StartsWith(@"D:\默认保存\下载\下载（待整理）\自动分类\DCIM\instagram\", r.DestinationPath);
    }
}