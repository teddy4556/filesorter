using FileSorter.Core;
using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

public class ClassifierServiceTests : IDisposable
{
    private readonly string _tmp;
    public ClassifierServiceTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "filesorter-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tmp);
    }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch {} }

    private RulesConfig Cfg() => RuleEngine.LoadFromString($"""
        version: 1
        default_action: move
        conflict_strategy: rename
        log_level: info
        destinations:
          images: {_tmp}\img
        rules:
          - name: pics
            type: extension
            patterns: [jpg]
            destination: images
        """);

    [Fact]
    public void Classify_Move_JpgFromSrcToDest()
    {
        var src = Path.Combine(_tmp, "photo.jpg");
        File.WriteAllText(src, "x");
        var svc = new ClassifierService(Cfg(), dryRun: false);
        var result = svc.ClassifyOne(src);
        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_tmp, "img", "photo.jpg")));
        Assert.False(File.Exists(src));
    }

    [Fact]
    public void Classify_DryRun_DoesNotMove()
    {
        var src = Path.Combine(_tmp, "photo.jpg");
        File.WriteAllText(src, "x");
        var svc = new ClassifierService(Cfg(), dryRun: true);
        var result = svc.ClassifyOne(src);
        Assert.True(result.Success);
        Assert.True(File.Exists(src));
        Assert.False(File.Exists(Path.Combine(_tmp, "img", "photo.jpg")));
    }

    [Fact]
    public void Classify_ConflictRename_AddsSuffix()
    {
        var dest = Path.Combine(_tmp, "img");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "photo.jpg"), "old");
        var src = Path.Combine(_tmp, "photo.jpg");
        File.WriteAllText(src, "new");
        var svc = new ClassifierService(Cfg(), dryRun: false);
        var result = svc.ClassifyOne(src);
        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(dest, "photo.jpg"))); // old
        Assert.True(File.Exists(Path.Combine(dest, "photo_1.jpg"))); // new
    }

    [Fact]
    public void Classify_AutoCreatesDestDir()
    {
        var src = Path.Combine(_tmp, "photo.jpg");
        File.WriteAllText(src, "x");
        // dest dir does NOT exist
        var svc = new ClassifierService(Cfg(), dryRun: false);
        svc.ClassifyOne(src);
        Assert.True(Directory.Exists(Path.Combine(_tmp, "img")));
    }
}