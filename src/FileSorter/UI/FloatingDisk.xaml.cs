using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace FileSorter.UI;

public partial class FloatingDisk : Window
{
    private System.Windows.Point _dragStart;
    private System.Windows.Point _windowStart;
    private bool _isDragging;

    // 2026-09-28: 注册表 HKCU\Software\FileSorter 存 FloatingDisk 位置(用户拖动后持久化)
    private const string RegistryKey = @"Software\FileSorter";

    // 2026-09-29: 把 Window 设成 system DPI aware (PerMonitorV2),否则 150% DPI 下 hit-test 错位
    // 参考:https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setprocessdpiawarenesscontext
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(int dpiAwarenessContext);

    private const int DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    public FloatingDisk()
    {
        // 必须在 Window.Show 之前调用
        try { SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); } catch { /* not supported */ }
        InitializeComponent();
        LoadIcon();
        LoadSavedPosition();
    }

    // 2026-09-28: 加载 app.ico(用户提供的黄色文件夹图标)作为漂浮磁盘图标
    private void LoadIcon()
    {
        var icoPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (!File.Exists(icoPath)) return;

        try
        {
            // WPF BitmapImage 不直接支持多尺寸 .ico,用 System.Drawing.Icon 提取再转换
            using var icon = new System.Drawing.Icon(icoPath);
            var bmp = icon.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;

            var wpfBmp = new BitmapImage();
            wpfBmp.BeginInit();
            wpfBmp.CacheOption = BitmapCacheOption.OnLoad;
            wpfBmp.StreamSource = ms;
            wpfBmp.EndInit();
            wpfBmp.Freeze();  // 冻结让 Image 跨线程安全 + WPF 不再尝试重渲染,消除拖动闪烁
            DiskImage.Source = wpfBmp;
        }
        catch { /* fall back to no image */ }
    }

    private void LoadSavedPosition()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKey);
            if (key?.GetValue("DiskX") is int x && key.GetValue("DiskY") is int y)
            {
                var work = SystemParameters.WorkArea;
                // 2026-09-28: 严格校验 saved 位置是否在所有屏幕内(防止用户拖出后重启看不到图标)
                var visible = x >= work.Left - 5 && x <= work.Right - Width + 5 &&
                              y >= work.Top - 5 && y <= work.Bottom - Height + 5;
                if (visible)
                {
                    Left = x;
                    Top = y;
                    return;
                }
            }
        }
        catch { /* fall through */ }
        PositionBottomRight();
    }

    private void SavePosition()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKey);
            key?.SetValue("DiskX", (int)Left);
            key?.SetValue("DiskY", (int)Top);
        }
        catch { /* ignore */ }
    }

    private void PositionBottomRight()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 20;
        Top = work.Bottom - Height - 20;
    }

    // 2026-09-28: 用户拖动 FloatingDisk 到任意屏幕位置
    private void OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _windowStart = new System.Windows.Point(Left, Top);
        _isDragging = true;
        CaptureMouse();
        // 拖动期间阻止 Image 重渲染(冻结后已无重渲染,但 Layout 仍会重画背景)
        // CacheHint=BitmapCache 已让 Image 在 GPU 层合成 — 移动 Window 只重画 window 边缘
        e.Handled = true;
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging) return;
        // 用屏幕坐标计算位移,避免窗口位置变化时 GetPosition(this) 偏差
        var screenPos = PointToScreen(e.GetPosition(this));
        var newLeft = screenPos.X - _dragStart.X;
        var newTop = screenPos.Y - _dragStart.Y;

        // 2026-09-28: 拖动期间把 window 限制在屏幕内(防止用户拖出可见区域导致图标消失)
        var work = SystemParameters.WorkArea;
        var maxLeft = work.Right - Width;
        var maxTop = work.Bottom - Height;
        if (newLeft < work.Left) newLeft = work.Left;
        if (newLeft > maxLeft) newLeft = maxLeft;
        if (newTop < work.Top) newTop = work.Top;
        if (newTop > maxTop) newTop = maxTop;

        Left = newLeft;
        Top = newTop;
    }

    private void OnMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ReleaseMouseCapture();
        SavePosition();
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
