using System.IO;
using System.Windows;

namespace FileSorter.UI;

public partial class FolderModeDialog : Window
{
    public enum Mode { Recursive, TopLevel, Cancel }
    public Mode Result { get; private set; } = Mode.Cancel;

    public FolderModeDialog(string folderPath)
    {
        InitializeComponent();
        PathLabel.Text = folderPath;
        var files = Directory.GetFiles(folderPath).Length;
        var dirs = Directory.GetDirectories(folderPath).Length;
        StatsLabel.Text = $"{files} 个顶层文件,{dirs} 个子文件夹";

        // 毛玻璃必须在窗口句柄创建后设置
        Loaded += (_, _) => ApplyGlass();
        SourceInitialized += (_, _) => ApplyGlass();
    }

    private void OnRecursive(object sender, RoutedEventArgs e) { Result = Mode.Recursive; Close(); }
    private void OnTopLevel(object sender, RoutedEventArgs e) { Result = Mode.TopLevel; Close(); }
    private void OnCancel(object sender, RoutedEventArgs e) { Result = Mode.Cancel; Close(); }

    // ================= 毛玻璃(2026-10-20 与其它窗口统一) =================

    private void ApplyGlass()
    {
        try
        {
            if (Dwm.TryEnableLegacyBlur(this)) Dwm.SetDarkTitleBar(this, true);
            Dwm.TrySetRoundedCorners(this);
        }
        catch { /* 材质失败不影响功能 */ }
    }
}
