using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using FileSorter.Core;
using Microsoft.Win32;

namespace FileSorter.UI;

public partial class EditPathsWindow : Window
{
    public class PathRow
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
    }

    private readonly string _rulesPath;
    public ObservableCollection<PathRow> Rows { get; } = new();

    public EditPathsWindow(string rulesPath)
    {
        InitializeComponent();
        _rulesPath = rulesPath;
        var cfg = RuleEngine.LoadFromFile(rulesPath);
        foreach (var kv in cfg.Destinations)
            Rows.Add(new PathRow { Name = kv.Key, Path = kv.Value });
        Rows.CollectionChanged += (_, _) => { };
        ((ItemsControl)FindName("Rows"))!.ItemsSource = Rows;
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PathRow row) return;
        var dlg = new OpenFolderDialog
        {
            Title = $"选择 {row.Name} 路径",
            InitialDirectory = System.IO.Directory.Exists(row.Path) ? row.Path : ""
        };
        if (dlg.ShowDialog(this) == true)
            row.Path = dlg.FolderName;
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PathRow row)
            row.Path = "";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var dict = Rows.ToDictionary(r => r.Name, r => r.Path);
        PathsEditor.SavePaths(_rulesPath, dict);
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnOpenYaml(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start("explorer.exe", $"\"{_rulesPath}\"");
    }
}
