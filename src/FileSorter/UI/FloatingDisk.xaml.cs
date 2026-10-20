using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace FileSorter.UI;

public partial class FloatingDisk : Window
{
    // _dragStart = 按下瞬间光标相对窗口左上角的 DIP 偏移。拖动全程不变,
    // 用于从「光标屏幕位置」反推「窗口左上角位置」。
    private System.Windows.Point _dragStart;
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
        _isDragging = true;
        CaptureMouse();
        // 拖动期间阻止 Image 重渲染(冻结后已无重渲染,但 Layout 仍会重画背景)
        // CacheHint=BitmapCache 已让 Image 在 GPU 层合成 — 移动 Window 只重画 window 边缘
        e.Handled = true;
    }

    // 2026-10-19 修 BUG(两轮):
    //   第一轮根因:老代码用 PointToScreen()(返回**物理像素**)减去 GetPosition()(**DIP**),
    //     150% DPI 下 newLeft 恒撞 clamp 上限 → 图标完全钉死在右下角,拖不动。
    //   第一轮修法(v1,有缺陷):改成 GetPosition 两次之差的 DIP 增量。
    //     但 GetPosition 返回**光标相对窗口**的坐标,窗口跟手移动后该值被自我抵消,
    //     窗口每帧永远落后光标一到两帧 —— 表现为"拖动距离和鼠标移动距离不是 1:1"。
    //   本轮修法(v2):锚定**光标在屏幕上的绝对位置**,窗口位置 = 光标屏幕位 - 按下时的抓取偏移。
    //     窗口移动不再反过来影响下一次计算,位移严格 1:1。
    //     关键:PointToScreen 返回物理像素,必须用 TransformFromDevice 显式转回 DIP 才能跟 Left/Top 同单位。
    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging) return;

        // capture 丢失(Alt+Tab / 焦点切换 / 在窗口外松手)时收到 LeftButton=Released,主动收尾
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) { EndDrag(); return; }

        // 光标的**屏幕**坐标(DIP)。用 CompositionTarget 的 TransformFromDevice 把
        // PointToScreen 的物理像素换算成 DIP —— 不换算就跟下面的 Left/Top 不同单位,重蹈 v0 覆辙。
        var src = PresentationSource.FromVisual(this);
        var screenDip = src?.CompositionTarget?.TransformFromDevice
                            .Transform(PointToScreen(e.GetPosition(this)))
                      ?? e.GetPosition(this);

        // 窗口左上角 = 光标屏幕位 - 抓取点偏移(抓取点是按下时的窗口内 DIP 坐标,全程不变)
        var newLeft = screenDip.X - _dragStart.X;
        var newTop = screenDip.Y - _dragStart.Y;

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
        EndDrag();
    }

    private void EndDrag()
    {
        if (!_isDragging) return;
        _isDragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
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
