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
