using FileSorter.Core;
using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

public class RulesFileWriterTests : IDisposable
{
    private readonly string _dir;

    public RulesFileWriterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "filesorter_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void FirstWrite_NoBackup()
    {
        var path = Path.Combine(_dir, "rules.yaml");
        var cfg = new RulesConfig { Version = 1 };
        RulesFileWriter.WriteWithBackup(path, cfg);

        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.bak.*"));
    }

    [Fact]
    public void Overwrite_CreatesBackup()
    {
        var path = Path.Combine(_dir, "rules.yaml");
        File.WriteAllText(path, "old content");
        RulesFileWriter.WriteWithBackup(path, new RulesConfig { Version = 1 });

        var baks = Directory.GetFiles(_dir, "*.bak.*");
        Assert.Single(baks);
    }

    [Fact]
    public void FourWrites_Keeps3Backups()
    {
        var path = Path.Combine(_dir, "rules.yaml");
        File.WriteAllText(path, "v0");
        for (int i = 1; i <= 4; i++)
        {
            RulesFileWriter.WriteWithBackup(path, new RulesConfig { Version = i });
            Thread.Sleep(1100);  // ensure unique yyyyMMdd-HHmmss suffix
        }

        var baks = Directory.GetFiles(_dir, "*.bak.*");
        Assert.Equal(3, baks.Length);
    }
}