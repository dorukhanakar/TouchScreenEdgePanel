using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using EdgePanel.Core;

namespace EdgePanel;

public partial class MainWindow : Window
{
    IntPtr _hwnd;

    // Touch → mouse promotion makes Windows move the real cursor onto the panel. The cursor position is
    // remembered when the first finger lands and put back shortly after the last finger lifts, so the
    // mouse stays where the user left it on the main screen.
    Native.POINT? _cursorBeforeTouch;
    int _activeTouches;
    readonly DispatcherTimer _cursorRestore = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(60) };

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
        _cursorRestore.Tick += (_, _) =>
        {
            _cursorRestore.Stop();
            if (_activeTouches == 0 && _cursorBeforeTouch is { } p) Native.SetCursorPos(p.X, p.Y);
            _cursorBeforeTouch = null;
        };
    }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.SetPanelStyle(_hwnd, AppSettings.Current.NoActivate);
        Native.DisableTouchFeedbackVisuals(_hwnd);   // no finger trails / tap circles on the panel
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
        const int WM_POINTERDOWN = 0x0246, WM_POINTERUP = 0x0247, WM_POINTERCAPTURECHANGED = 0x024C;
        const int MA_NOACTIVATE = 3, PA_NOACTIVATE = 3;

        if (msg is WM_POINTERDOWN or WM_POINTERUP or WM_POINTERCAPTURECHANGED)
            TrackTouchForCursor(msg == WM_POINTERDOWN, wParam);   // observe only; WPF still handles the input

        if (AppSettings.Current.NoActivate)
        {
            if (msg == WM_MOUSEACTIVATE) { handled = true; return (IntPtr)MA_NOACTIVATE; }
            if (msg == WM_POINTERACTIVATE) { handled = true; return (IntPtr)PA_NOACTIVATE; }
        }
        if (msg == WM_DISPLAYCHANGE)
            Dispatcher.BeginInvoke(PlaceOnMonitor, DispatcherPriority.Background);
        return IntPtr.Zero;
    }

    void TrackTouchForCursor(bool down, IntPtr wParam)
    {
        uint pointerId = (uint)((long)wParam & 0xFFFF);
        if (!Native.GetPointerType(pointerId, out var type) || type is not (Native.PT_TOUCH or Native.PT_PEN)) return;

        if (down)
        {
            _cursorRestore.Stop();
            // the cursor has not been moved yet when the first finger's WM_POINTERDOWN arrives
            if (_activeTouches++ == 0 && _cursorBeforeTouch == null && Native.GetCursorPos(out var p))
                _cursorBeforeTouch = p;
        }
        else if (_activeTouches > 0 && --_activeTouches == 0)
        {
            _cursorRestore.Start();   // after the synthesized mouse-up has been delivered
        }
    }
}
