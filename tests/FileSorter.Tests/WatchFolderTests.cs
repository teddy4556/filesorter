using FileSorter.Core;
using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

/// <summary>
/// 自动归类(监听文件夹)功能测试。
/// 重点覆盖:配置解析(双 key)、稳定期、防抖、未命中不动、循环保护。
/// </summary>
public class WatchFolderTests : IDisposable
{
    private readonly string _root;
    private readonly List<string> _log = new();

    public WatchFolderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fs-wf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private RulesConfig TestConfig(string inbox, string watchDir)
    {
        // default 兜底规则:所有文件都能命中 -> 验证「自动归类确实发生了」
        return RuleEngine.LoadFromString($$"""
            version: 1
            default_action: move
            conflict_strategy: rename
            log_level: info
            destinations:
              inbox: {{inbox}}
            rules:
              - name: catch-all
                type: default
                destination: inbox
            watch_folders:
              - path: {{watchDir}}
                enabled: true
                recursive: false
                mode: realtime
            """)!;
    }

    // ---------- 配置解析 ----------

    [Fact]
    public void LoadConfig_ReadsWatchFolders_SnakeCase()
    {
        var cfg = RuleEngine.LoadFromString($$"""
            version: 1
            destinations:
              inbox: C:\x
            rules:
              - name: c
                type: default
                destination: inbox
            watch_folders:
              - path: D:\a
                enabled: true
                recursive: true
                mode: realtime
                settle_seconds: 5
            """)!;

        var w = cfg.WatchFolders;
        Assert.Single(w);
        Assert.Equal("D:\\a", w[0].Path);
        Assert.True(w[0].Enabled);
        Assert.True(w[0].Recursive);
        Assert.Equal(5, w[0].SettleSeconds);
        Assert.Equal(5, w[0].EffectiveSettleSeconds);
    }

    [Fact]
    public void LoadConfig_ReadsWatchFolders_CamelCase()
    {
        // 现有 rules.yaml 由 RulesFileWriter 以 camelCase 序列化,这条必须也能读
        var cfg = RuleEngine.LoadFromString("""
            version: 1
            destinations:
              inbox: C:\x
            rules:
              - name: c
                type: default
                destination: inbox
            watchFolders:
              - path: D:\b
                enabled: true
                mode: Every5Minutes
                settleSeconds: 10
            """)!;

        var w = cfg.WatchFolders;
        Assert.Single(w);
        Assert.Equal("D:\\b", w[0].Path);
        Assert.Equal(10, w[0].SettleSeconds);
    }

    [Fact]
    public void LoadConfig_NoWatchFolders_YieldsEmptyList()
    {
        var cfg = RuleEngine.LoadFromString($$"""
            version: 1
            destinations:
              inbox: C:\x
            rules:
              - name: c
                type: default
                destination: inbox
            """)!;
        Assert.Empty(cfg.WatchFolders);
    }

    [Fact]
    public void LoadConfig_BothKeysSamePath_Dedupes()
    {
        var cfg = RuleEngine.LoadFromString("""
            version: 1
            destinations:
              inbox: C:\x
            rules:
              - name: c
                type: default
                destination: inbox
            watch_folders:
              - path: D:\same
            watchFolders:
              - path: D:\same
              - path: D:\other
            """)!;

        Assert.Equal(2, cfg.WatchFolders.Count);
    }

    // ---------- 间隔 / 稳定期默认值 ----------

    [Theory]
    [InlineData(WatchMode.Realtime, 0)]
    [InlineData(WatchMode.Every5Minutes, 300)]
    [InlineData(WatchMode.Every30Minutes, 1800)]
    public void EffectiveInterval_FollowsMode(WatchMode mode, int expected)
    {
        var w = new WatchFolder { Path = "D:\\x", Mode = mode };
        Assert.Equal(expected, w.EffectiveIntervalSeconds);
    }

    [Fact]
    public void EffectiveInterval_ExplicitOverrideWins()
    {
        var w = new WatchFolder { Path = "D:\\x", Mode = WatchMode.Every5Minutes, IntervalSeconds = 42 };
        Assert.Equal(42, w.EffectiveIntervalSeconds);
    }

    [Fact]
    public void EffectiveSettle_ZeroFallsBackToDefault()
    {
        var w = new WatchFolder { Path = "D:\\x", SettleSeconds = 0 };
        Assert.Equal(WatchFolderService.DefaultSettleSeconds, w.EffectiveSettleSeconds);
    }

    [Fact]
    public void EffectiveSettle_ExplicitWins()
    {
        var w = new WatchFolder { Path = "D:\\x", SettleSeconds = 30 };
        Assert.Equal(30, w.EffectiveSettleSeconds);
    }

    // ---------- 端到端:自动归类 ----------

    [Fact]
    public async Task AutoClassify_MovesFileIntoDestination()
    {
        var inbox = Path.Combine(_root, "inbox");
        var watchDir = Path.Combine(_root, "watch");
        Directory.CreateDirectory(watchDir);

        var file = Path.Combine(watchDir, "photo.jpg");
        await File.WriteAllTextAsync(file, "fake-jpeg-bytes");

        var cfg = TestConfig(inbox, watchDir);
        var log = new List<string>();
        var svc = new WatchFolderService(() => cfg, new CollectingLogger(log));
        svc.StartFromConfig();

        // 默认 settle 2s + 检查器 1s 周期,给足余量
        var ok = await WaitUntil(() => File.Exists(Path.Combine(inbox, "photo.jpg")), TimeSpan.FromSeconds(15));

        Assert.True(ok,
            "文件应被自动移入 destination。诊断日志:\n" +
            string.Join("\n", log.Select(l => "  " + l)) +
            $"\n  watchDir 内容: {string.Join(", ", Directory.GetFiles(watchDir).Select(Path.GetFileName))}" +
            $"\n  inbox 存在={Directory.Exists(inbox)} 内容={string.Join(", ", Directory.Exists(inbox) ? Directory.GetFiles(inbox).Select(Path.GetFileName) : new[]{"-"})}");
        Assert.False(File.Exists(file), "源文件应已移走");
    }

    [Fact]
    public async Task AutoClassify_UnmatchedFile_StaysPut()
    {
        // 没有 default 兜底规则的配置 -> 未命中应原地不动(用户拍板的策略)
        var watchDir = Path.Combine(_root, "watch2");
        Directory.CreateDirectory(watchDir);
        var file = Path.Combine(watchDir, "mystery.xyz");
        await File.WriteAllTextAsync(file, "data");

        var cfg = RuleEngine.LoadFromString($$"""
            version: 1
            default_action: move
            conflict_strategy: rename
            log_level: info
            destinations:
              jpg: {{Path.Combine(_root, "jpgs")}}
            rules:
              - name: only-jpg
                type: extension
                patterns:
                  - jpg
                destination: jpg
            """)!;

        using var svc = new WatchFolderService(() => cfg);
        svc.StartFromConfig();

        await Task.Delay(6000);

        Assert.True(File.Exists(file), "未命中规则的文件必须原地不动,不能被移动或删除");
    }

    [Fact]
    public async Task AutoClassify_SkipsFilesInsideDestination_LoopGuard()
    {
        // 监听目录本身就是 destination -> 移过去的文件不能被再次处理形成循环
        var dest = Path.Combine(_root, "loopdest");
        Directory.CreateDirectory(dest);
        var file = Path.Combine(dest, "already-there.jpg");
        await File.WriteAllTextAsync(file, "x");

        var cfg = RuleEngine.LoadFromString($$"""
            version: 1
            default_action: move
            conflict_strategy: rename
            log_level: info
            destinations:
              dest: {{dest}}
            rules:
              - name: catch
                type: default
                destination: dest
            """)!;

        using var svc = new WatchFolderService(() => cfg);
        svc.StartFromConfig();

        await Task.Delay(6000);

        Assert.True(File.Exists(file), "destination 内的文件不应被循环处理(改名/移走)");
        Assert.Empty(Directory.GetFiles(dest, "already-there_*.jpg"));
    }

    [Fact]
    public void ToggleOff_StopsWatching_ButKeepsConfig()
    {
        var watchDir = Path.Combine(_root, "watch3");
        Directory.CreateDirectory(watchDir);
        var cfg = RuleEngine.LoadFromString($$"""
            version: 1
            destinations:
              inbox: {{Path.Combine(_root, "inbox3")}}
            rules:
              - name: c
                type: default
                destination: inbox
            watch_folders:
              - path: {{watchDir}}
            """)!;

        using var svc = new WatchFolderService(() => cfg);
        svc.StartFromConfig();
        Assert.True(svc.ActiveCount > 0);

        svc.Enabled = false;
        Assert.Equal(0, svc.ActiveCount);

        svc.Enabled = true;
        Assert.True(svc.ActiveCount > 0);
    }

    [Fact]
    public void EnabledFalse_InConfig_Skipped()
    {
        var watchDir = Path.Combine(_root, "watch4");
        Directory.CreateDirectory(watchDir);
        var cfg = RuleEngine.LoadFromString($$"""
            version: 1
            destinations:
              inbox: {{Path.Combine(_root, "inbox4")}}
            rules:
              - name: c
                type: default
                destination: inbox
            watch_folders:
              - path: {{watchDir}}
                enabled: false
            """)!;

        using var svc = new WatchFolderService(() => cfg);
        svc.StartFromConfig();
        Assert.Equal(0, svc.ActiveCount);
    }

    [Fact]
    public void NonexistentFolder_IsSkipped_NotCrashing()
    {
        var cfg = RuleEngine.LoadFromString($$"""
            version: 1
            destinations:
              inbox: {{Path.Combine(_root, "inbox5")}}
            rules:
              - name: c
                type: default
                destination: inbox
            watch_folders:
              - path: Z:\\definitely\\not\\exist\\{{Guid.NewGuid():N}}
            """)!;

        using var svc = new WatchFolderService(() => cfg);
        svc.StartFromConfig();   // 不能抛
        Assert.Equal(0, svc.ActiveCount);
    }

    // ---------- 半拷贝防护 ----------

    [Fact]
    public async Task SettleDelay_LargeFile_NotMovedUntilStable()
    {
        var inbox = Path.Combine(_root, "inbox6");
        var watchDir = Path.Combine(_root, "watch6");
        Directory.CreateDirectory(watchDir);

        var file = Path.Combine(watchDir, "big.mp4");
        await File.WriteAllTextAsync(file, "chunk1");

        var cfg = RuleEngine.LoadFromString($$"""
            version: 1
            default_action: move
            conflict_strategy: rename
            log_level: info
            destinations:
              mp4: {{inbox}}
            rules:
              - name: mp4
                type: extension
                patterns:
                  - mp4
                destination: mp4
            watch_folders:
              - path: {{watchDir}}
                settle_seconds: 4
            """)!;

        using var svc = new WatchFolderService(() => cfg, new CollectingLogger(_log));
        svc.StartFromConfig();

        // 模拟持续写入:每次追加都改变大小,稳定期永远不满足
        for (var i = 0; i < 5; i++)
        {
            await Task.Delay(700);
            await File.AppendAllTextAsync(file, $"chunk{i + 2}-padding-padding");
            Assert.False(File.Exists(Path.Combine(inbox, "big.mp4")),
                "文件仍在写入时不应被移动(半拷贝防护)");
        }

        // 停止写入后等稳定期过去,应该被处理
        var done = await WaitUntil(() => File.Exists(Path.Combine(inbox, "big.mp4")), TimeSpan.FromSeconds(25));
        Assert.True(done,
            "写入停止且稳定期满后应被处理。诊断:\n" +
            string.Join("\n", _log.Select(l => "  " + l)) +
            $"\n  watchDir: {string.Join(", ", Directory.GetFiles(watchDir).Select(f => $"{Path.GetFileName(f)}({new FileInfo(f).Length}B)"))}" +
            $"\n  inbox6: {(Directory.Exists(inbox) ? string.Join(", ", Directory.GetFiles(inbox).Select(Path.GetFileName)) : "不存在")}" +
            $"\n  源文件 mtime 距今 = {(DateTime.UtcNow - new FileInfo(file).LastWriteTimeUtc).TotalSeconds:F1}s (settle 要求 4s)");
    }

    private sealed class CollectingLogger : ILogger
    {
        private readonly List<string> _sink;
        public CollectingLogger(List<string> sink) => _sink = sink;
        public void Info(string msg) { lock (_sink) _sink.Add("INFO " + msg); }
        public void Warn(string msg) { lock (_sink) _sink.Add("WARN " + msg); }
        public void Error(string msg) { lock (_sink) _sink.Add("ERROR " + msg); }
    }

    private static async Task<bool> WaitUntil(Func<bool> cond, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (cond()) return true;
            await Task.Delay(250);
        }
        return cond();
    }
}
