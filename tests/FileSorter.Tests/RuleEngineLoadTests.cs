using FileSorter.Core;
using Xunit;

namespace FileSorter.Tests;

public class RuleEngineLoadTests
{
    [Fact]
    public void Load_ValidYaml_ReturnsConfig()
    {
        var yaml = """
        version: 1
        default_action: move
        conflict_strategy: rename
        log_level: info
        destinations:
          images: D:\cat\img
          inbox: D:\cat\inbox
        rules:
          - name: pics
            type: extension
            patterns: [jpg, png]
            destination: images
          - name: catch-all
            type: default
            destination: inbox
        """;
        var cfg = RuleEngine.LoadFromString(yaml);
        Assert.Equal(1, cfg.Version);
        Assert.Equal("move", cfg.DefaultAction);
        Assert.Equal(2, cfg.Destinations.Count);
        Assert.Equal("D:\\cat\\img", cfg.Destinations["images"]);
        Assert.Equal(2, cfg.Rules.Count);
    }

    [Fact]
    public void Load_InvalidYaml_Throws()
    {
        var yaml = "this: is: not: valid: yaml:";
        Assert.ThrowsAny<Exception>(() => RuleEngine.LoadFromString(yaml));
    }

    [Fact]
    public void Load_PathTemplate_PreservesFields()
    {
        var yaml = """
        version: 1
        default_action: move
        conflict_strategy: rename
        log_level: info
        destinations:
          images: D:\cat\img
        rules:
          - name: sm-pic
            type: path_template
            extensions: [jpg, png]
            filename_pattern: '^([a-z]+)_\(@([^)]+)\)_'
            path: '{destinations.images}\{1}\{2}'
        """;
        var cfg = RuleEngine.LoadFromString(yaml);
        var r = cfg.Rules[0];
        Assert.Equal("path_template", r.Type);
        Assert.Equal(new[] { "jpg", "png" }, r.Extensions);
        Assert.Equal(@"^([a-z]+)_\(@([^)]+)\)_", r.FilenamePattern);
        Assert.Equal(@"{destinations.images}\{1}\{2}", r.Path);
    }
}