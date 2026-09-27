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
            filename_pattern: '^([a-z]+)_(@([^)]+))_'
            path: '{destinations.images}\{1}\{2}'
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
        var r = RuleEngine.Match(Cfg(), @"C:\in\twitter_(@hahaoy8)_肉丝儿_20260730.jpg");
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
}