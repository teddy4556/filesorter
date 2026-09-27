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

        // Best-effort SendTo auto-install on first run
        try
        {
            if (!Core.SendToInstaller.IsInstalled(GetExePath()))
                Core.SendToInstaller.Install(GetExePath());
        }
        catch { /* SendTo is non-critical */ }

        _tray = new TrayIcon();
        _tray.Initialize();
        _tray.EditPathsRequested += () => OpenEditPaths();
        _tray.EditRulesRequested += () => OpenRuleEditor();

        _disk = new FloatingDisk();
        _disk.FilesDropped += OnFilesDropped;
        _disk.Show();
    }

    private void HandleSendTo(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return;
        _rulesPath = EnsureRulesFile();
        _cfg = RuleEngine.LoadFromFile(_rulesPath);

        var dlg = new QuickRuleDialog(files, _cfg.Rules ?? new List<Rule>())
        {
            Owner = null  // top-level, no owner (avoid tying to a hidden main window)
        };
        if (dlg.ShowDialog() == true)
        {
            var svc = new ClassifierService(_cfg, dryRun: false);
            foreach (var f in files)
            {
                if (File.Exists(f))
                    svc.ClassifyOne(f);
            }
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