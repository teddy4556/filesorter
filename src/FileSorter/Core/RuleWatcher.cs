using System;
using System.IO;
using FileSorter.Core.Models;

namespace FileSorter.Core;

public class RuleWatcher : IDisposable
{
    private readonly string _path;
    private readonly Action<RulesConfig> _onReload;
    private readonly FileSystemWatcher _fsw;
    private DateTime _lastReloadUtc = DateTime.MinValue;
    private readonly TimeSpan _debounce = TimeSpan.FromSeconds(2);

    public RuleWatcher(string path, Action<RulesConfig> onReload)
    {
        _path = path;
        _onReload = onReload;
        var dir = System.IO.Path.GetDirectoryName(path)!;
        _fsw = new FileSystemWatcher(dir, System.IO.Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            EnableRaisingEvents = false
        };
        _fsw.Changed += (_, _) => TryReload();
        _fsw.Created += (_, _) => TryReload();
        _fsw.Renamed += (_, _) => TryReload();
    }

    public void Start() => _fsw.EnableRaisingEvents = true;
    public void Stop() => _fsw.EnableRaisingEvents = false;

    private void TryReload()
    {
        var now = DateTime.UtcNow;
        if (now - _lastReloadUtc < _debounce) return;
        _lastReloadUtc = now;
        try
        {
            var cfg = RuleEngine.LoadFromFile(_path);
            _onReload(cfg);
        }
        catch
        {
            // Keep old config silently; orchestrator may surface a toast later.
        }
    }

    public void Dispose() => _fsw.Dispose();
}