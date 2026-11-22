using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Perch.Core;

namespace Perch.Dock;

/// <summary>
/// A borderless window that sits exactly behind the dock bar and shows the blurred desktop through it.
/// It is separate from the dock window because blur always covers a whole window, and the dock window
/// is taller than the bar (it leaves room above the bar for magnified icons and labels).
/// </summary>
public sealed class GlassWindow : Window
{
    IntPtr _hwnd;
    Native.RECT _last;
    int _radius = -1;
    uint _tint;

    public GlassWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        Width = 10;
        Height = 10;
        Left = -32000;
        Top = -32000;
        Title = "Perch glass";
    }

    public IntPtr Handle => _hwnd;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        if (HwndSource.FromHwnd(_hwnd) is { CompositionTarget: { } ct }) ct.BackgroundColor = Colors.Transparent;
        var m = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        Native.DwmExtendFrameIntoClientArea(_hwnd, ref m);
        // Click-through, never activated, not in Alt+Tab.
        Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT);
        int noPeek = 1;
        Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_EXCLUDED_FROM_PEEK, ref noPeek, 4);
        int noRound = 1; // DWMWCP_DONOTROUND: the window region does the rounding
        Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref noRound, 4);
    }

    public void SetTint(uint abgr)
    {
        if (_hwnd == IntPtr.Zero || abgr == _tint) return;
        _tint = abgr;
        Native.SetAccent(_hwnd, Native.ACCENT_ENABLE_ACRYLICBLURBEHIND, abgr);
    }

    /// <summary>Moves the glass to a rectangle in physical pixels with the given corner radius (pixels).</summary>
    public void Place(Native.RECT r, int radius)
    {
        if (_hwnd == IntPtr.Zero) return;
        bool sizeChanged = r.Width != _last.Width || r.Height != _last.Height || radius != _radius;
        Native.SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, Native.SWP_NOACTIVATE | Native.SWP_NOZORDER | Native.SWP_SHOWWINDOW);
        if (sizeChanged)
        {
            var rgn = Native.CreateRoundRectRgn(0, 0, r.Width + 1, r.Height + 1, radius * 2, radius * 2);
            Native.SetWindowRgn(_hwnd, rgn, true); // the system owns the region afterwards
            _radius = radius;
        }
        _last = r;
    }

    public void HideGlass()
    {
        if (_hwnd != IntPtr.Zero) Native.ShowWindow(_hwnd, Native.SW_HIDE);
        _last = default;
    }
}
