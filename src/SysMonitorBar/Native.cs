using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SysMonitorBar;

internal static class Native
{
    // ---------------- 内存 ----------------
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public static bool TryGetMemory(out ulong totalBytes, out ulong availBytes)
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref m))
        {
            totalBytes = m.ullTotalPhys;
            availBytes = m.ullAvailPhys;
            return true;
        }
        totalBytes = availBytes = 0;
        return false;
    }

    // ---------------- 显示器刷新率 ----------------
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    private const int VREFRESH = 116;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    private const int ENUM_CURRENT_SETTINGS = -1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    /// <summary>主显示器当前模式的刷新率(Hz)，来自 EnumDisplaySettings。</summary>
    public static int GetPrimaryRefreshRateEx(out int width, out int height)
    {
        width = height = 0;
        try
        {
            var dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf<DEVMODE>();
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
            {
                width = dm.dmPelsWidth;
                height = dm.dmPelsHeight;
                if (dm.dmDisplayFrequency > 1) return dm.dmDisplayFrequency;
            }
        }
        catch { }
        return 0;
    }

    /// <summary>主显示器当前实际刷新率(Hz)，失败返回 0。</summary>
    public static int GetPrimaryRefreshRate()
    {
        int hz = 0;
        try
        {
            hz = GetPrimaryRefreshRateEx(out _, out _);
        }
        catch { }
        if (hz > 1) return hz;

        try
        {
            IntPtr dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return 0;
            try
            {
                hz = GetDeviceCaps(dc, VREFRESH);
                if (hz <= 1) hz = 60; // 某些虚拟显示器返回 0/1
                return hz;
            }
            finally { ReleaseDC(IntPtr.Zero, dc); }
        }
        catch { return 0; }
    }

    // ---------------- 窗口样式 ----------------
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private static IntPtr GetStyle(IntPtr hWnd, int idx)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, idx) : new IntPtr(GetWindowLong32(hWnd, idx));

    private static void SetStyle(IntPtr hWnd, int idx, IntPtr val)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, idx, val);
        else SetWindowLong32(hWnd, idx, val.ToInt32());
    }

    public static void SetClickThrough(IntPtr hWnd, bool enable)
    {
        if (hWnd == IntPtr.Zero) return;
        long ex = GetStyle(hWnd, GWL_EXSTYLE).ToInt64();
        long want = enable
            ? (ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE)
            : (ex & ~(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE));
        if (want != ex) SetStyle(hWnd, GWL_EXSTYLE, new IntPtr(want));
    }

    /// <summary>读取窗口扩展样式（诊断用）。</summary>
    public static long GetExStyle(IntPtr hWnd)
        => hWnd == IntPtr.Zero ? 0 : GetStyle(hWnd, GWL_EXSTYLE).ToInt64();

    /// <summary>鼠标穿透位是否确实生效。</summary>
    public static bool IsClickThrough(IntPtr hWnd)
        => (GetExStyle(hWnd) & WS_EX_TRANSPARENT) != 0;

    public static void MakeToolWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        long ex = GetStyle(hWnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_TOOLWINDOW;
        // 不要出现在 Alt+Tab
        ex &= ~0x00040000; // WS_EX_APPWINDOW
        SetStyle(hWnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    // ---------------- 逐像素 alpha 分层窗口 ----------------
    // 有了它才能让"背景透明、文字不透明"，这是 Form.Opacity（整窗均匀淡化）做不到的。

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int CX, CY; public SIZE(int w, int h) { CX = w; CY = h; } }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;
    private const int ULW_ALPHA = 0x02;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst,
        ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
        int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// 把一张 32bpp 预乘 alpha 的位图直接推给窗口合成器。
    /// 注意：一旦对某个窗口调用过 UpdateLayeredWindow，该窗口就改由这里驱动绘制，
    /// 系统不再发送 WM_PAINT，Form.Opacity / TransparencyKey 也会失效。
    /// </summary>
    public static bool PushLayeredBitmap(IntPtr hWnd, Bitmap bmp, int x, int y)
    {
        if (hWnd == IntPtr.Zero || bmp == null) return false;

        IntPtr screenDc = IntPtr.Zero, memDc = IntPtr.Zero, hBitmap = IntPtr.Zero, oldBitmap = IntPtr.Zero;
        try
        {
            screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) return false;
            memDc = CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero) return false;

            // 传 Color.FromArgb(0) 是关键：它保证 alpha=0 的像素保持全透明，
            // 传 Color.Empty 会被 GDI 填成不透明黑，透明背景就废了。
            hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            oldBitmap = SelectObject(memDc, hBitmap);

            var size = new SIZE(bmp.Width, bmp.Height);
            var srcLoc = new POINT(0, 0);
            var dstLoc = new POINT(x, y);
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA,
            };

            return UpdateLayeredWindow(hWnd, screenDc, ref dstLoc, ref size, memDc, ref srcLoc,
                0, ref blend, ULW_ALPHA);
        }
        catch { return false; }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                if (memDc != IntPtr.Zero && oldBitmap != IntPtr.Zero) SelectObject(memDc, oldBitmap);
                DeleteObject(hBitmap);
            }
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // ---------------- 置顶 ----------------
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    public static void BringToTop(IntPtr hWnd)
        => SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

    // ---------------- DPI ----------------
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    public static void EnablePerMonitorV2()
    {
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        catch { }
    }
}
