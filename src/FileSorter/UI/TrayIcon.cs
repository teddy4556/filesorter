using System.Drawing;
using System.IO;
using System.Windows;
using FileSorter.Core;

namespace FileSorter.UI;

public class TrayIcon : IDisposable
{
    private NotifyIcon? _notify;
    private ToolStripMenuItem? _autoStartItem;
    private ToolStripMenuItem? _autoWatchItem;

    public void Initialize()
    {
        // 2026-09-28: 用 app.ico 替换 SystemIcons.Application(用户提供的黄色文件夹)
        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        System.Drawing.Icon? icon = null;
        if (File.Exists(iconPath))
        {
            try { icon = new System.Drawing.Icon(iconPath); }
            catch { /* fall through to default */ }
        }
        _notify = new NotifyIcon
        {
            Icon = icon ?? SystemIcons.Application,
            Visible = true,
            Text = "FileSorter"
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开规则编辑器…", null, (_, _) => EditRulesRequested?.Invoke());
        menu.Items.Add("编辑路径…", null, (_, _) => EditPathsRequested?.Invoke());

        // 2026-10-19: 自动归类管理窗口(毛玻璃)—— 主功能,放最上面
        menu.Items.Add("自动归类设置…", null, (_, _) => OpenWatchManagerRequested?.Invoke());

        menu.Items.Add("打开 rules.yaml", null, (_, _) =>
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FileSorter", "rules.yaml");
            System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
        });
        menu.Items.Add(new ToolStripSeparator());

        // 2026-10-19: 自动归类总开关 —— 出问题时一键全停,不删配置
        _autoWatchItem = new ToolStripMenuItem("自动归类(监听文件夹)")
        {
            CheckOnClick = true,
            Checked = true
        };
        _autoWatchItem.Click += (_, _) =>
        {
            AutoWatchToggled?.Invoke(_autoWatchItem.Checked);
        };
        menu.Items.Add(_autoWatchItem);

        // v2.7.9 (2026-09-29): 开机启动 toggle
        _autoStartItem = new ToolStripMenuItem("开机启动")
        {
            CheckOnClick = true,
            Checked = AutoStart.IsInstalled()
        };
        _autoStartItem.Click += (_, _) =>
        {
            try
            {
                if (_autoStartItem.Checked) AutoStart.Install();
                else AutoStart.Uninstall();
                AutoStartToggled?.Invoke(_autoStartItem.Checked);
            }
            catch (Exception ex)
            {
                _autoStartItem.Checked = !_autoStartItem.Checked;
                System.Windows.MessageBox.Show($"切换失败:{ex.Message}",
                    "FileSorter", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("安装到右键菜单(发送到)…", null, (_, _) => InstallSendToRequested?.Invoke());
        menu.Items.Add("卸载右键菜单(发送)", null, (_, _) => UninstallSendToRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) =>
        {
            System.Windows.Application.Current.Shutdown();
        });
        _notify.ContextMenuStrip = menu;
    }

    public event Action? EditPathsRequested;
    public event Action? EditRulesRequested;
    public event Action? InstallSendToRequested;
    public event Action? UninstallSendToRequested;
    public event Action<bool>? AutoStartToggled;
    public event Action<bool>? AutoWatchToggled;

    /// <summary>2026-10-19: 打开自动归类管理窗口(毛玻璃)。</summary>
    public event Action? OpenWatchManagerRequested;

    public void Dispose()
    {
        _notify?.Dispose();
    }
}
