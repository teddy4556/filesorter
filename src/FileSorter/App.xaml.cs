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
        _rulesPath = EnsureRulesFile();
        _cfg = RuleEngine.LoadFromFile(_rulesPath);

        _tray = new TrayIcon();
        _tray.Initialize();
        _tray.EditPathsRequested += () => OpenEditPaths();

        _disk = new FloatingDisk();
        _disk.FilesDropped += OnFilesDropped;
        _disk.Show();
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
