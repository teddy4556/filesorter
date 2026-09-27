using System.Windows;

namespace FileSorter.UI;

public partial class FloatingDisk : Window
{
    public FloatingDisk()
    {
        InitializeComponent();
        PositionBottomRight();
    }

    private void PositionBottomRight()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 20;
        Top = work.Bottom - Height - 20;
    }

    private void OnDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop)!;
        FilesDropped?.Invoke(paths);
    }

    public event Action<string[]>? FilesDropped;
}
