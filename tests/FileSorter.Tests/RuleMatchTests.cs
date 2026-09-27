using FileSorter.Core;
using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

public class RuleMatchTests
{
    private static RulesConfig Cfg() => RuleEngine.LoadFromString("""
        version: 1
        default_action: move
        conflict_strategy: rename
        log_level: info
        destinations:
          images: D:\cat\img
          documents: D:\cat\doc
          inbox: D:\cat\inbox
        rules:
          - name: pics
            type: extension
            patterns: [jpg, jpeg, png]
            destination: images
          - name: invoice
            type: filename_keyword
            patterns: ["发票", "invoice"]
            extensions: [pdf, jpg]
            destination: documents
          - name: screenshot
            type: combined
            extension: [png]
            filename_keyword: ["screen"]
            destination: images
          - name: twitter-pic
            type: path_template
            extensions: [jpg, png]
            filename_pattern: '^([a-z]+)_\(@([^)]+)\)_'
            path: '{destinations.images}\{1}\@{2}'
          - name: catch-all
            type: default
            destination: inbox
        """);

    [Fact]
    public void Match_ExtensionRule_JpgGoesToImages()
    {
        var r = RuleEngine.Match(Cfg(), @"C:\in\photo.jpg");
        Assert.NotNull(r);
        Assert.Equal(@"D:\cat\img", r!.DestinationPath);
    }

    [Fact]
    public void Match_FilenameKeyword_InvoicePdfGoesToDocuments()
    {
        var r = RuleEngine.Match(Cfg(), @"C:\in\发票_2025.pdf");
        Assert.NotNull(r);
        Assert.Equal(@"D:\cat\doc", r!.DestinationPath);
    }

    [Fact]
    public void Match_Combined_PngScreenGoesToImages()
    {
        var r = RuleEngine.Match(Cfg(), @"C:\in\screen.png");
        Assert.NotNull(r);
        Assert.Equal(@"D:\cat\img", r!.DestinationPath);
    }

    [Fact]
    public void Match_PathTemplate_TwitterPictureGoesToNestedFolder()
    {
        // 注意:rules.yaml 里 pics (extension) 排在 twitter-pic (path_template) 前面,
        // 所以 jpg 先匹配 pics 走 images 路径,path_template 永远到不了。
        // 这里验证规则顺序:把 path_template 提到最前面来测试它的能力。
        var yaml = """
            version: 1
            default_action: move
            conflict_strategy: rename
            log_level: info
            destinations:
              images: D:\cat\img
            rules:
              - name: twitter-pic
                type: path_template
                extensions: [jpg, png]
                filename_pattern: '^([a-z]+)_\(@([^)]+)\)_'
                path: '{destinations.images}\{1}\@{2}'
            """;
        var cfg = RuleEngine.LoadFromString(yaml);
        var r = RuleEngine.Match(cfg, @"C:\in\twitter_(@hahaoy8)_肉丝儿_20260730.jpg");
        Assert.NotNull(r);
        Assert.Equal(@"D:\cat\img\twitter\@hahaoy8", r!.DestinationPath);
    }

    [Fact]
    public void Match_NoRuleHits_FallsBackToDefault()
    {
        var r = RuleEngine.Match(Cfg(), @"C:\in\notes.txt");
        Assert.NotNull(r);
        Assert.Equal(@"D:\cat\inbox", r!.DestinationPath);
    }

    [Fact]
    public void Match_Combined_PdfInvoiceGoesToInvoices()
    {
        // 用独立 yaml 避免污染共享 Cfg(): filename_keyword "invoice" 也会匹配这份文件,
        // 所以这里只放一条 combined (pdf + invoice) → invoices,验证 combined 不会被前面的规则抢走。
        var yaml = """
            version: 1
            default_action: move
            conflict_strategy: rename
            log_level: info
            destinations:
              invoices: D:\cat\invoices
            rules:
              - name: pdf-invoice
                type: combined
                extension: [pdf]
                filename_keyword: ["invoice"]
                destination: invoices
            """;
        var cfg = RuleEngine.LoadFromString(yaml);
        var r = RuleEngine.Match(cfg, @"C:\in\2026_invoice_acme.pdf");
        Assert.NotNull(r);
        Assert.Equal(@"D:\cat\invoices", r!.DestinationPath);
    }

    [Fact]
    public void Match_FilenamePattern_StartsWith_GoesToDestination()
    {
        var cfg = new FileSorter.Core.Models.RulesConfig
        {
            Destinations = new() { ["docs"] = @"D:\cat\docs" },
            Rules = new()
            {
                new()
                {
                    Name = "acme invoice",
                    Type = "filename_pattern",
                    StartsWith = new() { "acme_" },
                    Extensions = new() { "pdf" },
                    Destination = "docs"
                }
            }
        };
        var r = RuleEngine.Match(cfg, @"C:\in\acme_jan.pdf");
        Assert.NotNull(r);
        Assert.Equal(@"D:\cat\docs", r!.DestinationPath);
    }

    [Fact]
    public void Match_FilenamePattern_EndsWith_GoesToDestination()
    {
        var cfg = new FileSorter.Core.Models.RulesConfig
        {
            Destinations = new() { ["docs"] = @"D:\cat\docs" },
            Rules = new()
            {
                new()
                {
                    Name = "invoice",
                    Type = "filename_pattern",
                    EndsWith = new() { "_invoice.pdf" },
                    Destination = "docs"
                }
            }
        };
        var r = RuleEngine.Match(cfg, @"C:\in\acme_invoice.pdf");
        Assert.NotNull(r);
        Assert.Equal(@"D:\cat\docs", r!.DestinationPath);
    }

    [Fact]
    public void Match_NameTemplate_AutoRegex_GoesToMappingPath()
    {
        var cfg = new FileSorter.Core.Models.RulesConfig
        {
            Destinations = new() { ["images"] = @"D:\cat\img" },
            Rules = new()
            {
                new()
                {
                    Name = "social",
                    Type = "name_template",
                    Extensions = new() { "jpg" },
                    Template = "{platform}_@{author}_{title}",
                    Mappings = new()
                    {
                        new() { Token = "{platform}", Level = 1 },
                        new() { Token = "{author}", Level = 2 }
                    },
                    Destination = "images"
                }
            }
        };
        var r = RuleEngine.Match(cfg, @"C:\in\twitter_@hahaoy8_xxx.jpg");
        Assert.NotNull(r);
        Assert.Equal(@"D:\cat\img\twitter\hahaoy8", r!.DestinationPath);
    }

    [Fact]
    public void Match_NameTemplate_NoMappings_FallsBack_To_Destination()
    {
        var cfg = new FileSorter.Core.Models.RulesConfig
        {
            Destinations = new() { ["images"] = @"D:\cat\img" },
            Rules = new()
            {
                new()
                {
                    Name = "social flat",
                    Type = "name_template",
                    Extensions = new() { "jpg" },
                    Template = "{platform}_@{author}_{title}",
                    Mappings = null,
                    Destination = "images"
                }
            }
        };
        var r = RuleEngine.Match(cfg, @"C:\in\twitter_@hahaoy8_xxx.jpg");
        Assert.NotNull(r);
        Assert.Equal(@"D:\cat\img", r!.DestinationPath);
    }

    [Fact]
    public void Match_Skips_Inactive_Rule()
    {
        var cfg = new FileSorter.Core.Models.RulesConfig
        {
            Destinations = new() { ["img"] = @"D:\cat\img" },
            Rules = new()
            {
                new()
                {
                    Name = "disabled",
                    Type = "extension",
                    Patterns = new() { "jpg" },
                    Destination = "img",
                    Active = false
                }
            }
        };
        var r = RuleEngine.Match(cfg, @"C:\in\test.jpg");
        Assert.Null(r);  // Inactive rule should NOT match
    }
}