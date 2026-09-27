using FileSorter.Core;
using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

public class RuleWatcherTests : IDisposable
{
    private readonly string _tmp;
    public RuleWatcherTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "fs-watch-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tmp);
    }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch {} }

    [Fact]
    public async Task Watch_FileChanges_TriggersReload()
    {
        var path = Path.Combine(_tmp, "rules.yaml");
        File.WriteAllText(path, """
            version: 1
            default_action: move
            conflict_strategy: rename
            log_level: info
            destinations:
              inbox: C:\x
            rules:
              - name: catch
                type: default
                destination: inbox
            """);
        int reloads = 0;
        RulesConfig? last = null;
        using var w = new RuleWatcher(path, cfg => { reloads++; last = cfg; });
        w.Start();

        await Task.Delay(300); // let watcher initialize
        File.WriteAllText(path, """
            version: 1
            default_action: copy
            conflict_strategy: overwrite
            log_level: info
            destinations:
              inbox: C:\y
            rules:
              - name: catch
                type: default
                destination: inbox
            """);

        await Task.Delay(2500); // > 2s debounce

        Assert.True(reloads >= 1);
        Assert.NotNull(last);
        Assert.Equal("copy", last!.DefaultAction);
    }
}