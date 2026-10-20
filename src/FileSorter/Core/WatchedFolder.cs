using FileSorter.Core.Models;

namespace FileSorter.Core;

/// <summary>
/// 单个监听文件夹的运行时状态机。
///
/// 三种唤醒来源:
///   1. 定时轮询(WatchMode.Every5Minutes / Every30Minutes)—— 到点扫一遍全目录
///   2. FileSystemWatcher 事件(WatchMode.Realtime)—— 事件只当「提醒」用
///   3. 文件大小稳定检查器 —— 真正决定「什么时候可以处理」
///
/// 防半拷贝的核心:一个文件必须**连续 settleSeconds 秒内大小和 mtime 都不变**才会被处理。
/// 拷贝中的文件大小每帧都在涨,稳定期永远不满足,自然被推迟。
/// </summary>
public sealed class WatchedFolder : IDisposable
{
    private readonly WatchFolder _cfg;
    private readonly Action<string> _onReady;
    private readonly ILogger _log;
    private readonly HashSet<string> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly Dictionary<string, (long Size, DateTime MtimeUtc)> _pending = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _fsw;
    // 注意:本项目 csproj 同时开 UseWPF + UseWindowsForms,裸写 Timer 会在
    // System.Windows.Forms.Timer 和 System.Threading.Timer 之间报 CS0104 歧义,
    // 这里显式限定 System.Threading.Timer。
    private System.Threading.Timer? _pollTimer;
    private System.Threading.Timer? _settleTimer;
    private DateTime _lastPollUtc = DateTime.MinValue;
    private bool _disposed;

    public string FolderPath => _cfg.Path;
    public WatchFolder Config => _cfg;

    public WatchedFolder(WatchFolder cfg, Action<string> onReady, ILogger? log = null)
    {
        _cfg = cfg;
        _onReady = onReady;
        _log = log ?? new ConsoleLogger();
    }

    public void Start()
    {
        if (!Directory.Exists(_cfg.Path))
        {
            _log.Warn($"[watch] 目录不存在,跳过监听: {_cfg.Path}");
            return;
        }

        // 轮询模式:realtime 不用轮询,但首次启动也要扫一遍(捡起上次退出前落下的文件)
        if (_cfg.Mode != WatchMode.Realtime)
        {
            // Timer(TimerCallback, object, int dueTime, int period) —— 第 3/4 参都是 int 毫秒,
            // 没有 TimeSpan 重载(dueTime/period 全是 int)。别写成 TimeSpan,会报 CS1503。
            _pollTimer = new System.Threading.Timer(
                _ => Poll(), null, 1000,
                Math.Max(1000, _cfg.EffectiveIntervalSeconds * 1000));
        }

        // 实时模式:FSW 事件只用来「唤醒稳定性检查」,不直接触发处理
        if (_cfg.Mode == WatchMode.Realtime)
        {
            _fsw = new FileSystemWatcher(_cfg.Path)
            {
                IncludeSubdirectories = _cfg.Recursive,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024
            };
            _fsw.Created += OnFsEvent;
            _fsw.Changed += OnFsEvent;
            _fsw.Renamed += OnFsEvent;
            _fsw.EnableRaisingEvents = true;
        }

        // 稳定性检查器:每 1 秒扫一遍 pending 集合
        _settleTimer = new System.Threading.Timer(_ => CheckSettled(), null, 1000, 1000);

        _log.Info($"[watch] 启动监听 {_cfg.Path} (mode={_cfg.Mode}, settle={_cfg.EffectiveSettleSeconds}s, recursive={_cfg.Recursive})");

        // 启动时先登记一次现有文件,让上次退出前落下的也能被处理
        EnqueueExisting();
    }

    private void EnqueueExisting()
    {
        try
        {
            var opt = _cfg.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var f in Directory.EnumerateFiles(_cfg.Path, "*", opt))
                Enqueue(f);
        }
        catch (Exception ex)
        {
            _log.Warn($"[watch] 枚举 {_cfg.Path} 失败: {ex.Message}");
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        // Deleted 忽略(文件已被移走或用户删了)。其余一律当作「有新文件,去检查稳定性」。
        // Renamed 事件统一挂到 e.FullPath(RenamedFrom 指向旧路径,反正文件已不在监听目录内)。
        if (e.ChangeType == WatcherChangeTypes.Deleted) return;
        if (Directory.Exists(e.FullPath)) return;   // 子目录创建,不处理
        Enqueue(e.FullPath);
    }

    /// <summary>把文件丢进 pending,等待大小稳定。</summary>
    public void Enqueue(string filePath)
    {
        if (_disposed) return;
        var fi = new FileInfo(filePath);
        if (!fi.Exists || fi.Attributes.HasFlag(FileAttributes.Directory)) return;

        // 隐藏 / 系统文件直接跳过(桌面、缩略图缓存、Office 临时文件 ~$xxx)
        if (fi.Name.StartsWith("~$", StringComparison.Ordinal)) return;
        if (fi.Name.StartsWith(".", StringComparison.Ordinal)) return;
        if (fi.Attributes.HasFlag(FileAttributes.Hidden) || fi.Attributes.HasFlag(FileAttributes.System)) return;

        lock (_gate)
        {
            if (_inFlight.Contains(filePath)) return;
            _pending[filePath] = (fi.Length, fi.LastWriteTimeUtc);
        }
    }

    private void Poll()
    {
        if (_disposed) return;
        // 防抖:定时器可能提前触发,保证两次实际扫描间隔 >= 设定值
        var interval = TimeSpan.FromSeconds(_cfg.EffectiveIntervalSeconds);
        if (DateTime.UtcNow - _lastPollUtc < interval) return;
        _lastPollUtc = DateTime.UtcNow;
        _log.Info($"[watch] 轮询 {_cfg.Path}");
        EnqueueExisting();
    }

    private void CheckSettled()
    {
        if (_disposed) return;
        List<string> ready = new();

        lock (_gate)
        {
            var settle = TimeSpan.FromSeconds(_cfg.EffectiveSettleSeconds);
            var now = DateTime.UtcNow;
            var stillPending = new List<(string Path, long Size, DateTime MtimeUtc)>();

            foreach (var (path, (size, mtime)) in _pending)
            {
                FileInfo fi;
                try { fi = new FileInfo(path); }
                catch { continue; }

                if (!fi.Exists) continue; // 已被处理掉 / 手动删了

                // 大小或 mtime 变了 -> 还在写,重新计时
                if (fi.Length != size || fi.LastWriteTimeUtc != mtime)
                {
                    stillPending.Add((path, fi.Length, fi.LastWriteTimeUtc));
                    continue;
                }

                // 大小稳定满 settle 期 -> 可以处理
                if (now - fi.LastWriteTimeUtc >= settle)
                    ready.Add(path);
                else
                    stillPending.Add((path, fi.Length, fi.LastWriteTimeUtc));
            }

            _pending.Clear();
            foreach (var p in stillPending) _pending[p.Path] = (p.Size, p.MtimeUtc);
            foreach (var r in ready) _inFlight.Add(r);
        }

        foreach (var r in ready)
        {
            try { _onReady(r); }
            catch (Exception ex) { _log.Error($"[watch] 处理 {r} 抛异常: {ex.Message}"); }
            finally { lock (_gate) _inFlight.Remove(r); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _fsw?.Dispose(); } catch { }
        try { _pollTimer?.Dispose(); } catch { }
        try { _settleTimer?.Dispose(); } catch { }
    }
}
