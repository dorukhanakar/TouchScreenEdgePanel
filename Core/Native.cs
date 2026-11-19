using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EdgePanel.Core;

public static class Native
{
    // ── monitors ─────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFOEX mi);

    public sealed record MonitorInfo(int Index, string Device, RECT Bounds, bool Primary)
    {
        public int Width => Bounds.Right - Bounds.Left;
        public int Height => Bounds.Bottom - Bounds.Top;
        public override string ToString() => L.MonitorName(Index + 1, Width, Height, Primary);
    }

    public static List<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        MonitorEnumProc proc = (IntPtr h, IntPtr dc, ref RECT r, IntPtr d) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(h, ref mi))
                list.Add(new MonitorInfo(list.Count, mi.szDevice, mi.rcMonitor, (mi.dwFlags & 1) != 0));
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
        GC.KeepAlive(proc);
        return list;
    }

    /// <summary>Configured monitor, otherwise the ultra-wide one (Xeneon Edge is 2560x720), otherwise a secondary.</summary>
    public static MonitorInfo? PickMonitor(int index)
    {
        var m = GetMonitors();
        if (index >= 0 && index < m.Count) return m[index];
        return m.FirstOrDefault(x => x.Width >= x.Height * 3)
            ?? m.FirstOrDefault(x => !x.Primary)
            ?? m.FirstOrDefault();
    }

    /// <summary>
    /// Opens a dialog centred on the primary monitor's work area. The panel lives on the 720 px tall Edge screen,
    /// where a settings window would not fit (and typing there is awkward).
    /// </summary>
    public static void CenterOnPrimary(Window w)
    {
        void Place()
        {
            var wa = SystemParameters.WorkArea;   // primary monitor, in DIPs
            double width = w.ActualWidth > 0 ? w.ActualWidth : w.Width;
            double height = w.ActualHeight > 0 ? w.ActualHeight : (double.IsNaN(w.Height) ? 600 : w.Height);
            w.Left = wa.Left + Math.Max(0, (wa.Width - width) / 2);
            w.Top = wa.Top + Math.Max(0, (wa.Height - height) / 2);
        }
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        Place();                                   // before showing: lands on the primary screen
        w.Loaded += (_, _) => Place();             // after layout: exact centre with the real size
    }

    // ── touch: cursor + feedback visuals ─────────────────────────
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    public const int PT_TOUCH = 2, PT_PEN = 3;
    [DllImport("user32.dll")] public static extern bool GetPointerType(uint pointerId, out int pointerType);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool SetWindowFeedbackSetting(IntPtr hwnd, int feedback, uint flags, uint size, ref int config);

    /// <summary>Turns off Windows' touch/pen feedback visuals (contact trails, tap and press-and-hold circles) for one window.</summary>
    public static void DisableTouchFeedbackVisuals(IntPtr hwnd)
    {
        // FEEDBACK_TOUCH_CONTACTVISUALIZATION (1) … FEEDBACK_GESTURE_PRESSANDTAP (11)
        for (int feedback = 1; feedback <= 11; feedback++)
        {
            int off = 0;
            try { SetWindowFeedbackSetting(hwnd, feedback, 0, sizeof(int), ref off); } catch { }
        }
    }

    // ── window placement / styles ────────────────────────────────
    public const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr v);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);

    const int GWL_EXSTYLE = -20;
    const long WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>Tool window = hidden from Alt+Tab. No-activate = tapping the panel doesn't pull focus out of a game.</summary>
    public static void SetPanelStyle(IntPtr h, bool noActivate)
    {
        long s = (long)GetWindowLongPtr(h, GWL_EXSTYLE);
        s |= WS_EX_TOOLWINDOW;
        if (noActivate) s |= WS_EX_NOACTIVATE; else s &= ~WS_EX_NOACTIVATE;
        SetWindowLongPtr(h, GWL_EXSTYLE, (IntPtr)s);
    }

    // ── shell icons (exe / lnk / url → large icon with alpha) ────
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        public uint c0, c1, c2, c3;
    }

    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int c, out BITMAP bm);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFO bmi, uint usage);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);

    public static BitmapSource? GetShellImage(string path, int size)
    {
        IShellItemImageFactory? f = null;
        IntPtr hbm = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f);
            const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4, SIIGBF_SCALEUP = 0x100;
            if (f.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK | SIIGBF_SCALEUP, out hbm) != 0 || hbm == IntPtr.Zero)
                return null;
            return FromHBitmap(hbm);
        }
        catch { return null; }
        finally
        {
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
            if (f != null) Marshal.ReleaseComObject(f);
        }
    }

    // CreateBitmapSourceFromHBitmap drops alpha, so copy the 32bpp pixels ourselves (top-down).
    static BitmapSource? FromHBitmap(IntPtr hbm)
    {
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var bm) == 0) return null;
        int w = bm.bmWidth, h = bm.bmHeight, stride = w * 4;
        var bits = new byte[stride * h];
        var bi = new BITMAPINFO { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var dc = GetDC(IntPtr.Zero);
        try { if (GetDIBits(dc, hbm, 0, (uint)h, bits, ref bi, 0) == 0) return null; }
        finally { ReleaseDC(IntPtr.Zero, dc); }

        bool anyAlpha = false;
        for (int i = 3; i < bits.Length; i += 4) if (bits[i] != 0) { anyAlpha = true; break; }
        if (!anyAlpha) for (int i = 3; i < bits.Length; i += 4) bits[i] = 255;

        BitmapSource src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, bits, stride);
        if (FindFramedIcon(bits, w, h) is { } inner)
            src = new CroppedBitmap(src, inner);
        src.Freeze();
        return src;
    }

    /// <summary>
    /// Apps that only ship a small icon come back as a tiny glyph inside a thin square frame.
    /// Detect that frame and return the rectangle of the icon inside it, so the tile can scale it up.
    /// </summary>
    static Int32Rect? FindFramedIcon(byte[] px, int w, int h)
    {
        bool Opaque(int x, int y) => px[(y * w + x) * 4 + 3] > 16;

        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (Opaque(x, y)) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
        if (x1 - x0 < 16 || y1 - y0 < 16) return null;

        // frame = top edge almost fully opaque, but the row 4px below it is mostly empty
        int edge = 0, below = 0, span = x1 - x0 + 1;
        for (int x = x0; x <= x1; x++)
        {
            if (Opaque(x, y0)) edge++;
            if (Opaque(x, y0 + 4)) below++;
        }
        if (edge < span * 0.9 || below > span * 0.3) return null;

        int ix0 = w, iy0 = h, ix1 = -1, iy1 = -1;
        for (int y = y0 + 3; y <= y1 - 3; y++)
            for (int x = x0 + 3; x <= x1 - 3; x++)
                if (Opaque(x, y)) { ix0 = Math.Min(ix0, x); ix1 = Math.Max(ix1, x); iy0 = Math.Min(iy0, y); iy1 = Math.Max(iy1, y); }
        if (ix1 < ix0 || iy1 < iy0) return null;

        // square crop centred on the inner icon
        int size = Math.Max(ix1 - ix0, iy1 - iy0) + 1;
        int cx = (ix0 + ix1) / 2, cy = (iy0 + iy1) / 2;
        int sx = Math.Clamp(cx - size / 2, 0, w - size), sy = Math.Clamp(cy - size / 2, 0, h - size);
        return new Int32Rect(sx, sy, size, size);
    }
}
