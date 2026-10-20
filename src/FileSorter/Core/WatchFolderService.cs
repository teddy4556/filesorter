using FileSorter.Core.Models;

namespace FileSorter.Core;

/// <summary>
/// 自动归类协调器:管理 N 个 <see cref="WatchedFolder"/>,并把「到了稳定期的文件」交给 ClassifierService。
///
/// 循环保护:如果一个监听目录同时是某条规则的 destination(或其父/子目录),
/// 移过去的文件会再次被监听捕获 -> 无限循环。
/// 做法:处理前算出所有 destination 的规范化路径集合,命中就跳过该文件。
/// </summary>
public class WatchFolderService : IDisposable
{
    /// <summary>稳定期默认值(秒)。用户可在 watch_folders 条目里用 settleSeconds 覆盖。</summary>
    public const int DefaultSettleSeconds = 2;

    private readonly List<WatchedFolder> _folders = new();
    private readonly ILogger _log;
    private readonly Func<RulesConfig> _cfgProvider;
    private bool _enabled = true;
    private bool _disposed;

    public WatchFolderService(Func<RulesConfig> cfgProvider, ILogger? log = null)
    {
        _cfgProvider = cfgProvider;
        _log = log ?? new ConsoleLogger();
    }

    /// <summary>tray 总开关。关掉时所有监听立即停,config 保留。</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (value) StartAll();
            else StopAll();
            _log.Info($"[watch] 自动归类总开关 -> {(value ? "开" : "关")}");
        }
    }

    public int ActiveCount
    {
        get { lock (_folders) return _folders.Count; }
    }

    // ===== 运行时统计(2026-10-19 为 UI 状态卡加)=====
    private int _processedCount;
    private int _unmatchedCount;
    private int _failedCount;
    private DateTime _lastProcessedUtc = DateTime.MinValue;
    private readonly Dictionary<string, int> _perFolderCount = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTime _startedUtc = DateTime.UtcNow;

    /// <summary>本次会话已成功归类的文件数。</summary>
    public int ProcessedCount => Volatile.Read(ref _processedCount);

    /// <summary>未命中规则、原地不动的文件数。</summary>
    public int UnmatchedCount => Volatile.Read(ref _unmatchedCount);

    /// <summary>处理失败(IO 错误等)的文件数。</summary>
    public int FailedCount => Volatile.Read(ref _failedCount);

    /// <summary>最近一次成功归类的 UTC 时间;从未成功过 = DateTime.MinValue。</summary>
    public DateTime LastProcessedUtc => _lastProcessedUtc;

    /// <summary>本次会话运行时长。</summary>
    public TimeSpan Uptime => DateTime.UtcNow - _startedUtc;

    /// <summary>按监听目录统计已处理数(键为目录路径)。</summary>
    public IReadOnlyDictionary<string, int> PerFolderCount
    {
        get { lock (_perFolderCount) return new Dictionary<string, int>(_perFolderCount); }
    }

    /// <summary>重置统计(UI 上「清零」按钮用)。</summary>
    public void ResetStats()
    {
        Volatile.Write(ref _processedCount, 0);
        Volatile.Write(ref _unmatchedCount, 0);
        Volatile.Write(ref _failedCount, 0);
        _lastProcessedUtc = DateTime.MinValue;
        lock (_perFolderCount) _perFolderCount.Clear();
    }

    /// <summary>按当前配置启动(App 启动时调)。</summary>
    public void StartFromConfig()
    {
        if (_disposed) return;
        if (!_enabled) return;
        StartAll();
    }

    /// <summary>按当前配置重建全部监听。配置文件热重载后调用。</summary>
    public void Reload(RulesConfig cfg)
    {
        StopAll();
        if (!_enabled) return;
        StartAll();
    }

    private void StartAll()
    {
        RulesConfig cfg;
        try { cfg = _cfgProvider(); }
        catch (Exception ex) { _log.Warn($"[watch] 读配置失败: {ex.Message}"); return; }

        var list = cfg.WatchFolders ?? new List<WatchFolder>();
        var enabled = list.Where(w => w.Enabled && !string.IsNullOrWhiteSpace(w.Path)).ToList();

        // 目录不存在的直接剔掉,不要塞进 _folders —— 否则 ActiveCount 会虚高,
        // 且 tray 上显示的监听数对不上实际。WatchedFolder.Start() 内部也会兜底再查一次。
        var existing = enabled.Where(w => Directory.Exists(w.Path)).ToList();
        foreach (var missing in enabled.Except(existing))
            _log.Warn($"[watch] 目录不存在,已跳过: {missing.Path}");

        lock (_folders)
        {
            _folders.Clear();
            foreach (var w in existing)
            {
                try
                {
                    var wf = new WatchedFolder(w, HandleReady, _log);
                    wf.Start();
                    _folders.Add(wf);
                }
                catch (Exception ex)
                {
                    _log.Error($"[watch] 启动监听失败 {w.Path}: {ex.Message}");
                }
            }
        }
        _log.Info($"[watch] 已启动 {_folders.Count} 个监听目录");
    }

    private void StopAll()
    {
        lock (_folders)
        {
            foreach (var f in _folders) { try { f.Dispose(); } catch { } }
            _folders.Clear();
        }
    }

    /// <summary>所有 destination 的规范化路径集合 —— 用于循环保护。</summary>
    private HashSet<string> BuildDestinationSet(RulesConfig cfg)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, raw) in cfg.Destinations ?? new Dictionary<string, string>())
            if (!string.IsNullOrWhiteSpace(raw)) set.Add(NormalizePath(raw));
        foreach (var r in cfg.Rules ?? new List<Rule>())
        {
            var d = ResolveRuleDestination(cfg, r);
            if (!string.IsNullOrWhiteSpace(d)) set.Add(NormalizePath(d));
        }
        return set;
    }

    private static string? ResolveRuleDestination(RulesConfig cfg, Rule r)
    {
        if (!string.IsNullOrWhiteSpace(r.Destination))
        {
            // 可能是 alias,也可能是绝对路径
            if (cfg.Destinations.TryGetValue(r.Destination, out var resolved)) return resolved;
            return r.Destination;
        }
        if (!string.IsNullOrWhiteSpace(r.DestinationAlias)
            && cfg.Destinations.TryGetValue(r.DestinationAlias, out var byAlias)) return byAlias;
        return null;
    }

    private static string NormalizePath(string p)
    {
        try
        {
            var full = Path.GetFullPath(p.Trim().Trim('"'));
            return full.TrimEnd(Path.DirectorySeparatorChar);
        }
        catch { return p.Trim().TrimEnd(Path.DirectorySeparatorChar); }
    }

    /// <summary>稳定性检查通过 -> 执行归类。</summary>
    private void HandleReady(string filePath)
    {
        RulesConfig cfg;
        try { cfg = _cfgProvider(); }
        catch (Exception ex) { _log.Warn($"[watch] 处理前读配置失败: {ex.Message}"); return; }

        var dests = BuildDestinationSet(cfg);

        // --- 循环保护:文件本身在 destination 目录里 -> 不再处理 ---
        var normFile = NormalizePath(filePath);
        foreach (var d in dests)
        {
            if (normFile.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                _log.Info($"[watch] 跳过(位于目标目录内,防循环): {filePath}");
                return;
            }
        }

        var svc = new ClassifierService(cfg, dryRun: false, _log);
        var res = svc.ClassifyOne(filePath);
        if (res.Success)
        {
            Interlocked.Increment(ref _processedCount);
            _lastProcessedUtc = DateTime.UtcNow;
            // 找出是哪个监听目录处理的,给 UI 的「监听明细」用
            var folder = FindOwningFolder(filePath);
            if (folder is not null)
            {
                lock (_perFolderCount)
                    _perFolderCount[folder] = _perFolderCount.TryGetValue(folder, out var c) ? c + 1 : 1;
            }
            _log.Info($"[watch] 自动归类 {Path.GetFileName(filePath)}");
        }
        // 未命中 -> ClassifierService 返回 no rule matched,文件原地不动(用户拍板的策略)
        else if (!string.Equals(res.Error, "no rule matched", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _failedCount);
            _log.Warn($"[watch] {Path.GetFileName(filePath)} 处理失败: {res.Error}");
        }
        else
        {
            Interlocked.Increment(ref _unmatchedCount);
            _log.Info($"[watch] {Path.GetFileName(filePath)} 未命中规则,原地不动");
        }
    }

    /// <summary>找出这个文件属于哪个监听目录(最长前缀匹配,处理 recursive 场景)。</summary>
    private string? FindOwningFolder(string filePath)
    {
        string? best = null;
        var bestLen = -1;
        lock (_folders)
        {
            foreach (var f in _folders)
            {
                var p = NormalizePath(f.FolderPath);
                if (NormalizePath(filePath).StartsWith(p, StringComparison.OrdinalIgnoreCase)
                    && p.Length > bestLen)
                {
                    best = p;
                    bestLen = p.Length;
                }
            }
        }
        return best;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAll();
    }
}
