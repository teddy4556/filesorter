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
    }

    private void OnRecursive(object sender, RoutedEventArgs e) { Result = Mode.Recursive; Close(); }
    private void OnTopLevel(object sender, RoutedEventArgs e) { Result = Mode.TopLevel; Close(); }
    private void OnCancel(object sender, RoutedEventArgs e) { Result = Mode.Cancel; Close(); }
}
