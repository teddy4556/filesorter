using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FileSorter.UI;

/// <summary>
/// DWM 毛玻璃 / 圆角 / 标题栏助手。
///
/// 核心结论(2026-10-19 在 Win11 26H2 build 26300 上实测):
///   <see cref="TryEnableLegacyBlur"/> 是唯一在 AllowsTransparency=True 的 WPF
///   窗口上稳定生效的材质路径。Win11 专属的 Mica / Acrylic
///   (DWMWA_SYSTEMBACKDROP_TYPE) API 返回 0 但 DWM 不给 layered 窗口渲染背景材质,
///   实际看不见效果 —— 别用。
///
/// ⚠️ dwFlags 必须带 DWM_BB_ENABLE(1):传 0 会返回 0x80070057 (E_INVALIDARG)。
///    错误码看着像「系统不支持」,实际是「0 本身就是非法值」。hRgnBlur 传 NULL
///    反而能成功(全窗口模糊不需要 region);要传 region 得配 DWM_BB_BLURREGION(2)。
/// </summary>
internal static class Dwm
{
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    private const int DWM_BB_ENABLE = 0x00000001;
    private const int DWM_BB_BLURREGION = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_BLURBEHIND
    {
        public int dwFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fEnable;
        public IntPtr hRgnBlur;
        [MarshalAs(UnmanagedType.Bool)] public bool fTransitionOnMouse;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DWM_BLURBEHIND bb);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>全窗口毛玻璃。调用方必须已设 AllowsTransparency=True + Background=Transparent。</summary>
    public static bool TryEnableLegacyBlur(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            var bb = new DWM_BLURBEHIND
            {
                dwFlags = DWM_BB_ENABLE,   // ← 关键,不是 0
                fEnable = true,
                hRgnBlur = IntPtr.Zero,
                fTransitionOnMouse = false
            };
            return DwmEnableBlurBehindWindow(hwnd, ref bb) == 0;
        }
        catch { return false; }
    }

    /// <summary>只模糊窗口的某一块区域。</summary>
    public static bool TryEnableLegacyBlurRegion(Window w, int left, int top, int right, int bottom)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            IntPtr region = CreateRectRgn(left, top, right, bottom);
            try
            {
                var bb = new DWM_BLURBEHIND
                {
                    dwFlags = DWM_BB_ENABLE | DWM_BB_BLURREGION,
                    fEnable = true,
                    hRgnBlur = region,
                    fTransitionOnMouse = false
                };
                return DwmEnableBlurBehindWindow(hwnd, ref bb) == 0;
            }
            finally { if (region != IntPtr.Zero) DeleteObject(region); }
        }
        catch { return false; }
    }

    /// <summary>关闭毛玻璃。</summary>
    public static void DisableLegacyBlur(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            var bb = new DWM_BLURBEHIND
            {
                dwFlags = DWM_BB_ENABLE,
                fEnable = false,
                hRgnBlur = IntPtr.Zero,
                fTransitionOnMouse = false
            };
            DwmEnableBlurBehindWindow(hwnd, ref bb);
        }
        catch { }
    }

    /// <summary>Win11 圆角窗口。失败不影响功能,只是直角。</summary>
    public static bool TrySetRoundedCorners(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            int v = DWMWCP_ROUND;
            return DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref v, sizeof(int)) == 0;
        }
        catch { return false; }
    }

    /// <summary>标题栏图标深/浅色。半透明窗口配深色图标才看得清。</summary>
    public static void SetDarkTitleBar(Window w, bool dark)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            int v = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));
        }
        catch { }
    }
}
