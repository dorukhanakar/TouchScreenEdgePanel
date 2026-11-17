using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using EdgePanel.Core;

namespace EdgePanel;

public partial class MainWindow : Window
{
    IntPtr _hwnd;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        // stop the whole window from "bouncing" when a touch scroll hits the end of a list
        ManipulationBoundaryFeedback += (_, e) => e.Handled = true;
        AppSettings.Changed += () =>
        {
            Native.SetPanelStyle(_hwnd, AppSettings.Current.NoActivate);
            PlaceOnMonitor();
        };
    }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.SetPanelStyle(_hwnd, AppSettings.Current.NoActivate);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        PlaceOnMonitor();
    }

    public void PlaceOnMonitor()
    {
        var m = Native.PickMonitor(AppSettings.Current.MonitorIndex);
        if (m == null || _hwnd == IntPtr.Zero) return;
        Native.SetWindowPos(_hwnd, IntPtr.Zero, m.Bounds.Left, m.Bounds.Top, m.Width, m.Height,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEACTIVATE = 0x0021, WM_POINTERACTIVATE = 0x024B, WM_DISPLAYCHANGE = 0x007E;
        const int MA_NOACTIVATE = 3, PA_NOACTIVATE = 3;

        if (AppSettings.Current.NoActivate)
        {
            if (msg == WM_MOUSEACTIVATE) { handled = true; return (IntPtr)MA_NOACTIVATE; }
            if (msg == WM_POINTERACTIVATE) { handled = true; return (IntPtr)PA_NOACTIVATE; }
        }
        if (msg == WM_DISPLAYCHANGE)
            Dispatcher.BeginInvoke(PlaceOnMonitor, DispatcherPriority.Background);
        return IntPtr.Zero;
    }
}
