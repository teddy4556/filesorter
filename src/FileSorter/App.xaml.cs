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

        // Normal startup
        _rulesPath = EnsureRulesFile();
        _cfg = RuleEngine.LoadFromFile(_rulesPath);

        // Best-effort SendTo auto-install on first run (v2: write to stderr so failures are visible)
        try
        {
            if (!Core.SendToInstaller.IsInstalled(GetExePath()))
            {
                Core.SendToInstaller.Install(GetExePath());
                Console.Error.WriteLine($"[FileSorter] SendTo 已安装: %USERPROFILE%\\SendTo\\FileSorter.lnk");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FileSorter] SendTo 安装失败: {ex.Message}");
        }

        _tray = new TrayIcon();
        _tray.Initialize();
        _tray.EditPathsRequested += () => OpenEditPaths();
        _tray.EditRulesRequested += () => OpenRuleEditor();
        _tray.InstallSendToRequested += () => InstallSendToFromMenu();
        _tray.UninstallSendToRequested += () => UninstallSendToFromMenu();

        _disk = new FloatingDisk();
        _disk.FilesDropped += OnFilesDropped;
        _disk.Show();
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
        // 直到命中。QuickRuleDialog 不再弹 — 用户在 tray icon 拖入路径时才会弹。
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
        // After save, reload config from disk (RuleWatcher would also catch it within 2s).
        try { _cfg = RuleEngine.LoadFromFile(_rulesPath); }
        catch { /* keep old */ }
    }

    private void OpenRuleEditor()
    {
        if (_rulesPath is null) return;
        var editor = new RuleEditor(_rulesPath, _cfg);
        editor.ShowDialog();
        // After save, reload so in-memory state matches disk.
        try { _cfg = RuleEngine.LoadFromFile(_rulesPath); }
        catch { /* keep old */ }
    }

    private static string GetRulesPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FileSorter", "rules.yaml");

    private static string GetExePath()
    {
        // SingleFile-publish: Assembly.Location is null; use the live process module path.
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
            // Copy template from exe directory
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
        """;

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}