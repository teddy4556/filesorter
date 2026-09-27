using System.Drawing;
using System.IO;
using System.Windows;

namespace FileSorter.UI;

public class TrayIcon : IDisposable
{
    private NotifyIcon? _notify;

    public void Initialize()
    {
        _notify = new NotifyIcon
        {
            Icon = SystemIcons.Application,  // WPF apps: Assembly.Location is null in XAML entry; use system default
            Visible = true,
            Text = "FileSorter"
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open rules…", null, (_, _) => EditRulesRequested?.Invoke());
        menu.Items.Add("Open paths…", null, (_, _) => EditPathsRequested?.Invoke());
        menu.Items.Add("Open rules.yaml", null, (_, _) =>
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FileSorter", "rules.yaml");
            System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) =>
        {
            System.Windows.Application.Current.Shutdown();
        });
        _notify.ContextMenuStrip = menu;
    }

    public event Action? EditPathsRequested;
    public event Action? EditRulesRequested;

    public void Dispose()
    {
        _notify?.Dispose();
    }
}
