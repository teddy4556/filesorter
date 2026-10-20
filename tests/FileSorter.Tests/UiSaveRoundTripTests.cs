using FileSorter.Core;
using FileSorter.Core.Models;
using FileSorter.UI;
using Xunit;

namespace FileSorter.Tests;

/// <summary>
/// UI 保存链路测试(2026-10-20 新增)。
///
/// 为什么必须有这组测试:今天在「自动归类设置」窗口上连踩三个 bug ——
/// YAML 重复 key、WatchFolders getter 返回副本、双别名属性字段名不匹配 ——
/// 而 118 个测试**全部通过**。原因很简单:那些测试都直接调
/// WatchFolderService / RuleEngine,完全绕过了 UI 的「改内存 → 写盘 → 读回」
/// 这条链。功能坏着,测试是绿的。
///
/// 这组测试跑的是真实代码路径(WatchFolderWindow.SaveAndVerify /
/// MutateWatchFolders),专门盯住那个链路上出的问题。
/// </summary>
public class UiSaveRoundTripTests : IDisposable
{
    private readonly string _root;
    private string _inbox => Path.Combine(_root, "inbox");

    public UiSaveRoundTripTests()
    {
        // ⚠️ 路径格式有讲究,踩了两次才知道:
        //   1) `C:\Temp\x` 反斜杠 —— 本来没问题(YAML plain scalar 不转义反斜杠)
        //   2) 改成 `C:/Temp/x` 正斜杠 —— **反而炸了**,因为 plain scalar 里的 ':'
        //      是 key 分隔符,`C:/` 后面跟路径会被当成嵌套映射,报
        //      "found invalid mapping"。报错跟"路径格式"毫无关系,极难定位。
        // 所以固定用 `X:\...` 反斜杠写法(跟项目里其它测试一致)。
        _root = @"D:\fsub-" + Guid.NewGuid().ToString("N")[..8];
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best-effort */ }
    }

    private string RulesPath => Path.Combine(_root, "rules.yaml");

    /// <summary>最小可用配置:一条兜底规则 + N 个监听目录。</summary>
    private RulesConfig ConfigWith(params WatchFolder[] folders)
    {
        // ⚠️ 这里刻意用普通字符串拼接而不是 raw string($$""")。
        // raw string 会按结束标记 """ 的位置剥离公共缩进,而后面 += 追加的片段
        // 不带那层缩进 —— 两段缩进不一致,YAML 直接报
        // "found invalid mapping"。踩了三次才定位到,报错信息跟缩进毫无关系。
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("version: 1");
        sb.AppendLine("default_action: move");
        sb.AppendLine("conflict_strategy: rename");
        sb.AppendLine("log_level: info");
        sb.AppendLine("destinations:");
        sb.AppendLine("  inbox: " + _inbox);
        sb.AppendLine("rules:");
        sb.AppendLine("  - name: catch-all");
        sb.AppendLine("    type: default");
        sb.AppendLine("    destination: inbox");
        if (folders.Length > 0)
        {
            sb.AppendLine("watch_folders:");
            foreach (var f in folders)
            {
                sb.AppendLine("  - path: " + f.Path);
                sb.AppendLine("    enabled: " + f.Enabled.ToString().ToLower());
                sb.AppendLine("    recursive: " + f.Recursive.ToString().ToLower());
                sb.AppendLine("    mode: " + ToYamlMode(f.Mode));
            }
        }
        return RuleEngine.LoadFromString(sb.ToString());
    }

    /// <summary>WatchMode -> YAML 字面量(小写)。
    /// 项目里所有现有测试都写小写(realtime / every5minutes)。</summary>
    private static string ToYamlMode(WatchMode m) => m switch
    {
        WatchMode.Realtime => "realtime",
        WatchMode.Every5Minutes => "every5minutes",
        WatchMode.Every30Minutes => "every30minutes",
        _ => m.ToString().ToLower()
    };

    private static WatchFolder Folder(string path, WatchMode mode = WatchMode.Realtime,
        bool enabled = true, bool recursive = false) => new()
    {
        Path = path,
        Enabled = enabled,
        Recursive = recursive,
        Mode = mode
    };

    // ============================================================
    // 1. 写盘往返 —— 挡住「保存成功但读回是空的」
    // ============================================================

    [Fact]
    public void SaveAndVerify_SingleFolder_RoundTrips()
    {
        var cfg = ConfigWith(Folder(Path.Combine(_root, "inbox")));
        File.WriteAllText(RulesPath, "version: 1\n");   // 让 writer 走「覆盖已有文件」分支

        var fresh = WatchFolderWindow.SaveAndVerify(RulesPath, cfg);

        Assert.NotNull(fresh);
        Assert.Single(fresh!.WatchFolders);
        Assert.Equal(Path.Combine(_root, "inbox"), fresh.WatchFolders[0].Path);
    }

    [Fact]
    public void SaveAndVerify_ThreeFolders_AllSurvive()
    {
        var cfg = ConfigWith(
            Folder(Path.Combine(_root, "a"), WatchMode.Realtime),
            Folder(Path.Combine(_root, "b"), WatchMode.Every5Minutes),
            Folder(Path.Combine(_root, "c"), WatchMode.Every30Minutes, enabled: false, recursive: true));

        var fresh = WatchFolderWindow.SaveAndVerify(RulesPath, cfg);

        Assert.NotNull(fresh);
        Assert.Equal(3, fresh!.WatchFolders.Count);
    }

    /// <summary>今天 bug #1 的正面回归:YAML 里出现两行 watchFolders 时,
    /// 后者覆盖前者,读回来 0 个目录 —— 用户看到的是「保存了但没生效」。</summary>
    [Fact]
    public void SaveAndVerify_WrittenYaml_HasExactlyOneWatchKey()
    {
        var cfg = ConfigWith(Folder(Path.Combine(_root, "inbox")));
        WatchFolderWindow.SaveAndVerify(RulesPath, cfg);

        var lines = File.ReadAllLines(RulesPath);
        var watchKeys = lines.Count(l =>
            l.TrimStart().StartsWith("watch_folders", StringComparison.Ordinal) ||
            l.TrimStart().StartsWith("watchFolders", StringComparison.Ordinal));

        Assert.True(watchKeys == 1,
            $"rules.yaml 里出现了 {watchKeys} 个 watch key,必须是 1 个(重复 key 会导致读回覆盖丢失)。" +
            $"实际行:{string.Join(" | ", lines.Where(l => l.Contains("atch")))}");
    }

    /// <summary>子目录字段也不能出现双份(settle_seconds 和 settleSeconds 各写一遍)。</summary>
    [Fact]
    public void SaveAndVerify_SettleFields_NotDuplicated()
    {
        var f = Folder(Path.Combine(_root, "inbox"));
        f.SettleSeconds = 7;
        f.IntervalSeconds = 90;
        var cfg = ConfigWith(f);

        WatchFolderWindow.SaveAndVerify(RulesPath, cfg);

        var text = File.ReadAllText(RulesPath);
        Assert.DoesNotContain("settle_seconds", text);
        Assert.DoesNotContain("interval_seconds", text);
        Assert.Contains("settleSeconds", text);
    }

    /// <summary>空列表也要写出这个键 —— 否则重启后读回来是 null 而不是空列表。</summary>
    [Fact]
    public void SaveAndVerify_EmptyList_StillWritesKey()
    {
        var cfg = ConfigWith();   // 一个监听目录都没有

        var fresh = WatchFolderWindow.SaveAndVerify(RulesPath, cfg);

        Assert.NotNull(fresh);
        Assert.Empty(fresh!.WatchFolders);
        Assert.Contains("watchFolders", File.ReadAllText(RulesPath));
    }

    // ============================================================
    // 2. getter 返回副本 —— 挡住「内存改了但写盘还是 0 个」
    // ============================================================

    /// <summary>今天 bug #2:WatchFolders 的 getter 每次 .ToList() 返回**新对象**,
    /// 直接 Add 改的是临时副本,setter 从未被调用。这条测试把这个语义钉死。</summary>
    [Fact]
    public void WatchFolders_Getter_ReturnsCopy_NotLiveList()
    {
        var cfg = ConfigWith(Folder(Path.Combine(_root, "inbox")));

        var first = cfg.WatchFolders;
        first.Add(Folder(Path.Combine(_root, "ghost")));

        // 副本被改了,底层没变
        Assert.Single(cfg.WatchFolders);
    }

    [Fact]
    public void MutateWatchFolders_Add_Then_Persists()
    {
        var cfg = ConfigWith(Folder(Path.Combine(_root, "a")));

        WatchFolderWindow.MutateWatchFolders(cfg, list => list.Add(Folder(Path.Combine(_root, "b"))));

        Assert.Equal(2, cfg.WatchFolders.Count);
        var fresh = WatchFolderWindow.SaveAndVerify(RulesPath, cfg);
        Assert.NotNull(fresh);
        Assert.Equal(2, fresh!.WatchFolders.Count);
    }

    [Fact]
    public void MutateWatchFolders_RemoveAt_Then_Persists()
    {
        var cfg = ConfigWith(Folder(Path.Combine(_root, "a")), Folder(Path.Combine(_root, "b")));

        WatchFolderWindow.MutateWatchFolders(cfg, list => list.RemoveAt(0));

        Assert.Single(cfg.WatchFolders);
        var fresh = WatchFolderWindow.SaveAndVerify(RulesPath, cfg);
        Assert.NotNull(fresh);
        Assert.Single(fresh!.WatchFolders);
        Assert.Equal(Path.Combine(_root, "b"), fresh!.WatchFolders[0].Path);
    }

    [Fact]
    public void MutateWatchFolders_ToggleEnabled_Then_Persists()
    {
        var cfg = ConfigWith(Folder(Path.Combine(_root, "a"), enabled: true));

        WatchFolderWindow.MutateWatchFolders(cfg, list => list[0].Enabled = false);

        var fresh = WatchFolderWindow.SaveAndVerify(RulesPath, cfg);
        Assert.NotNull(fresh);
        Assert.False(fresh!.WatchFolders[0].Enabled);
    }

    [Fact]
    public void MutateWatchFolders_EditMode_ThenPersists()
    {
        var cfg = ConfigWith(Folder(Path.Combine(_root, "a"), WatchMode.Realtime));

        WatchFolderWindow.MutateWatchFolders(cfg, list => list[0].Mode = WatchMode.Every5Minutes);

        var fresh = WatchFolderWindow.SaveAndVerify(RulesPath, cfg);
        Assert.NotNull(fresh);
        Assert.Equal(WatchMode.Every5Minutes, fresh!.WatchFolders[0].Mode);
    }

    /// <summary>连续多次改(模拟 UI 上反复开关/移除)不能把配置搞坏。</summary>
    [Fact]
    public void MutateWatchFolders_RepeatedEdits_StayConsistent()
    {
        var cfg = ConfigWith(Folder(Path.Combine(_root, "a")));

        WatchFolderWindow.MutateWatchFolders(cfg, l => l.Add(Folder(Path.Combine(_root, "b"))));
        WatchFolderWindow.MutateWatchFolders(cfg, l => l[0].Enabled = false);
        WatchFolderWindow.MutateWatchFolders(cfg, l => l.RemoveAt(1));
        WatchFolderWindow.MutateWatchFolders(cfg, l => l.Add(Folder(Path.Combine(_root, "c"))));

        var fresh = WatchFolderWindow.SaveAndVerify(RulesPath, cfg);
        Assert.NotNull(fresh);
        Assert.Equal(2, fresh!.WatchFolders.Count);
        Assert.False(fresh.WatchFolders[0].Enabled);
        Assert.Equal(Path.Combine(_root, "c"), fresh.WatchFolders[1].Path);
    }

    // ============================================================
    // 3. 双 key 读取 —— 挡住「用户手写的 snake_case 配置读不回来」
    // ============================================================

    [Theory]
    [InlineData("watch_folders")]
    [InlineData("watchFolders")]
    public void LoadFromString_AcceptsBothKeyStyles(string key)
    {
        var yaml = "version: 1\n" +
                   "default_action: move\n" +
                   "destinations:\n" +
                   "  inbox: " + _inbox + "\n" +
                   "rules:\n" +
                   "  - name: catch-all\n" +
                   "    type: default\n" +
                   "    destination: inbox\n" +
                   key + ":\n" +
                   "  - path: " + _inbox + "\n" +
                   "    enabled: true\n" +
                   "    recursive: false\n" +
                   "    mode: realtime\n";

        var cfg = RuleEngine.LoadFromString(yaml);
        Assert.Single(cfg.WatchFolders);
    }

    /// <summary>写盘用 camelCase,读回也要认 —— 往返之后下一次保存不能丢配置。</summary>
    [Fact]
    public void SaveThenResave_DoesNotLoseFolders()
    {
        var cfg = ConfigWith(Folder(Path.Combine(_root, "a")), Folder(Path.Combine(_root, "b")));
        Assert.NotNull(WatchFolderWindow.SaveAndVerify(RulesPath, cfg));

        // 第二次保存:模拟用户改完再存一次
        var loaded = RuleEngine.LoadFromFile(RulesPath);
        WatchFolderWindow.MutateWatchFolders(loaded, l => l[0].Enabled = false);

        var fresh = WatchFolderWindow.SaveAndVerify(RulesPath, loaded);
        Assert.NotNull(fresh);
        Assert.Equal(2, fresh!.WatchFolders.Count);
        Assert.False(fresh.WatchFolders[0].Enabled);
    }
}
