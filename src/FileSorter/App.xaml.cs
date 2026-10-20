using System.IO;
using System.Windows;
using FileSorter.Core;
using FileSorter.Core.Models;
using FileSorter.UI;
using Application = System.Windows.Application;

namespace FileSorter;

public partial class App : Application
{
    private TrayIcon? _tray;
    private FloatingDisk? _disk;
    private RulesConfig _cfg = new();
    private string? _rulesPath;
    private WatchFolderService? _watch;
    private WatchFolderWindow? _watchWindow;
    private FileLogger? _fileLog;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = Core.StartupArgs.Parse(e.Args);
        switch (args.Mode)
        {
            case Core.StartupMode.Install:
                Core.AutoStart.Install();
                Shutdown();
                return;

            case Core.StartupMode.Uninstall:
                Core.AutoStart.Uninstall();
                Shutdown();
                return;

            case Core.StartupMode.InstallSendTo:
                try { Core.SendToInstaller.Install(GetExePath()); }
                catch (Exception ex) { Console.Error.WriteLine($"[install-sendto] {ex.Message}"); }
                Shutdown();
                return;

            case Core.StartupMode.SendTo:
                HandleSendTo(args.Files);
                Shutdown();
                return;
        }

        // 2026-10-19: --watch-ui 直接打开自动归类管理窗口(桌面快捷方式用)。
        // 先于 Normal startup 处理 —— 走的是完整初始化路径(读配置 + 启动监听 + 托盘),
        // 只是在初始化完成后多开一个窗口,这样从快捷方式启动也能看到实时统计。
        var wantWatchUi = e.Args.Any(a =>
            string.Equals(a, "--watch-ui", StringComparison.OrdinalIgnoreCase));
        // --rules-ui 直接打开规则编辑器(跟 --watch-ui 同性质,方便做桌面快捷方式)
        var wantRuleUi = e.Args.Any(a =>
            string.Equals(a, "--rules-ui", StringComparison.OrdinalIgnoreCase));

        // Normal startup
        _rulesPath = EnsureRulesFile();
        _cfg = RuleEngine.LoadFromFile(_rulesPath);

        // 2026-10-19: 文件日志落到 %APPDATA%\FileSorter\filesorter.log ——
        // 自动归类在后台跑,Console 输出在 WinExe 下没人看得到,出问题时必须能查。
        _fileLog = new FileLogger(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileSorter", "filesorter.log"));
        _fileLog.Info("=== FileSorter 启动 ===");

        // Best-effort SendTo auto-install on first run
        try
        {
            if (!Core.SendToInstaller.IsInstalled(GetExePath()))
            {
                Core.SendToInstaller.Install(GetExePath());
                _fileLog.Info("SendTo 已安装");
            }
        }
        catch (Exception ex) { _fileLog.Warn($"SendTo 安装失败: {ex.Message}"); }

        _tray = new TrayIcon();
        _tray.Initialize();
        _tray.EditPathsRequested += () => OpenEditPaths();
        _tray.EditRulesRequested += () => OpenRuleEditor();
        _tray.InstallSendToRequested += () => InstallSendToFromMenu();
        _tray.UninstallSendToRequested += () => UninstallSendToFromMenu();

        // 2026-10-19: 自动归类 —— 指定文件夹,文件一落地就归类
        _watch = new WatchFolderService(() => _cfg, _fileLog);
        _watch.StartFromConfig();
        _tray.AutoWatchToggled += on => SetAutoWatch(on);

        // 2026-10-19: 自动归类管理窗口(毛玻璃)
        _tray.OpenWatchManagerRequested += OpenWatchManager;

        _disk = new FloatingDisk();
        _disk.FilesDropped += OnFilesDropped;
        _disk.Show();

        if (wantWatchUi) OpenWatchManager();
        if (wantRuleUi) OpenRuleEditor();
    }

    /// <summary>打开自动归类管理窗口。已开就前置,不重复开第二个。</summary>
    private void OpenWatchManager()
    {
        if (_watchWindow is not null)
        {
            if (_watchWindow.WindowState == WindowState.Minimized)
                _watchWindow.WindowState = WindowState.Normal;
            _watchWindow.Activate();
            return;
        }
        _watchWindow = new WatchFolderWindow(_rulesPath!, _watch);
        _watchWindow.Closed += (_, _) => _watchWindow = null;
        _watchWindow.Show();
    }

    private void SetAutoWatch(bool on)
    {
        if (_watch is null) return;
        _watch.Enabled = on;
        _fileLog?.Info($"自动归类已{(on ? "开启" : "关闭")},当前监听 {_watch.ActiveCount} 个目录");
    }

    /// <summary>配置文件变更后热重载监听列表(tray 编辑器保存后调用)。</summary>
    private void ReloadConfigAndWatch()
    {
        if (_rulesPath is null) return;
        try
        {
            _cfg = RuleEngine.LoadFromFile(_rulesPath);
            _watch?.Reload(_cfg);
        }
        catch { /* keep old */ }
    }

    private void InstallSendToFromMenu()
    {
        try
        {
            var exe = GetExePath();
            if (Core.SendToInstaller.IsInstalled(exe))
            {
                System.Windows.MessageBox.Show("已经安装到右键菜单(发送)了。",
                    "FileSorter", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Core.SendToInstaller.Install(exe);
            System.Windows.MessageBox.Show(
                $"已安装到右键菜单(发送)。\n\n位置:%USERPROFILE%\\SendTo\\FileSorter.lnk\n\n现在任意文件右键 → 发送到 → FileSorter 即可触发分类。",
                "FileSorter", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"安装失败:{ex.Message}",
                "FileSorter", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UninstallSendToFromMenu()
    {
        try
        {
            if (Core.SendToInstaller.Uninstall())
            {
                System.Windows.MessageBox.Show("已从右键菜单(发送)卸载 FileSorter。",
                    "FileSorter", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show("右键菜单没有 FileSorter,无需卸载。",
                    "FileSorter", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"卸载失败:{ex.Message}",
                "FileSorter", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void HandleSendTo(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return;
        _rulesPath = EnsureRulesFile();
        _cfg = RuleEngine.LoadFromFile(_rulesPath);

        // 2026-09-28: SendTo 自动 first-match-wins 按 rules.yaml 顺序轮询每个 rule
        var svc = new ClassifierService(_cfg, dryRun: false);
        foreach (var f in files)
        {
            if (File.Exists(f))
                svc.ClassifyOne(f);
        }
    }

    private void OnFilesDropped(string[] paths)
    {
        var svc = new ClassifierService(_cfg, dryRun: false);
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                var dlg = new FolderModeDialog(p) { Owner = _disk };
                if (dlg.ShowDialog() != true || dlg.Result == FolderModeDialog.Mode.Cancel) continue;

                var opt = dlg.Result == FolderModeDialog.Mode.Recursive
                    ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (var f in Directory.EnumerateFiles(p, "*", opt))
                    svc.ClassifyOne(f);
            }
            else
            {
                svc.ClassifyOne(p);
            }
        }
    }

    private void OpenEditPaths()
    {
        if (_rulesPath is null) return;
        var w = new EditPathsWindow(_rulesPath);
        w.ShowDialog();
        ReloadConfigAndWatch();
    }

    private void OpenRuleEditor()
    {
        if (_rulesPath is null) return;
        var editor = new RuleEditor(_rulesPath, _cfg);
        editor.ShowDialog();
        ReloadConfigAndWatch();
    }

    private static string GetRulesPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FileSorter", "rules.yaml");

    private static string GetExePath()
    {
        return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot determine exe path");
    }

    private static string EnsureRulesFile()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileSorter");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, "rules.yaml");
        if (!File.Exists(dest))
        {
            var src = Path.Combine(AppContext.BaseDirectory, "rules.yaml");
            if (File.Exists(src))
                File.Copy(src, dest);
            else
                File.WriteAllText(dest, DefaultYaml());
        }
        return dest;
    }

    private static string DefaultYaml() => """
        version: 1
        default_action: move
        conflict_strategy: rename
        log_level: info
        destinations:
          inbox: <USERPROFILE>\Documents\FileSorter-Inbox
        rules:
          - name: catch-all
            type: default
            destination: inbox
        # 自动归类:指定文件夹,文件落地即归类(未命中规则则原地不动)
        watch_folders:
          - path: <USERPROFILE>\Downloads\待整理
            enabled: false          # 改 true 开启
            recursive: false
            mode: realtime          # realtime | Every5Minutes | Every30Minutes
            settle_seconds: 2       # 文件大小稳定几秒才处理(防半拷贝);大视频调大
        """;

    protected override void OnExit(ExitEventArgs e)
    {
        try { _watch?.Dispose(); } catch { }
        try { _tray?.Dispose(); } catch { }
        _fileLog?.Info("=== FileSorter 退出 ===");
        base.OnExit(e);
    }
}
