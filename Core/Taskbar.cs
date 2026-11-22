using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Perch.Core;

/// <summary>
/// Hides the Windows taskbar while Perch runs and puts it back afterwards.
/// The taskbar is switched to auto-hide (so it stops reserving screen space) and its windows are hidden.
/// The original auto-hide state is saved to settings first, so a crash is repaired on the next start.
/// </summary>
public static class Taskbar
{
    static DispatcherTimer? _guard;
    public static bool IsHidden { get; private set; }

    static IntPtr Primary => Native.FindWindow("Shell_TrayWnd", null);

    static List<IntPtr> AllTaskbars()
    {
        var list = new List<IntPtr>();
        var p = Primary;
        if (p != IntPtr.Zero) list.Add(p);
        IntPtr h = IntPtr.Zero;
        while ((h = Native.FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) list.Add(h);
        return list;
    }

    static int GetState()
    {
        var abd = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = Primary };
        return (int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref abd);
    }

    static void SetState(int state)
    {
        var abd = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = Primary, lParam = new IntPtr(state) };
        Native.SHAppBarMessage(Native.ABM_SETSTATE, ref abd);
    }

    public static void Hide()
    {
        if (Primary == IntPtr.Zero) return;
        var s = Settings.Current;
        if (s.TaskbarOriginalState < 0)
        {
            s.TaskbarOriginalState = GetState();
            Settings.SaveNow(); // before touching anything, so a crash can be undone
        }
        SetState(Native.ABS_AUTOHIDE | (s.TaskbarOriginalState & Native.ABS_ALWAYSONTOP));
        foreach (var h in AllTaskbars()) Native.ShowWindow(h, Native.SW_HIDE);
        IsHidden = true;

        if (_guard == null)
        {
            // Explorer shows the taskbar again for Start, some notifications and after it restarts.
            _guard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _guard.Tick += (_, _) => Enforce();
        }
        _guard.Start();
    }

    static void Enforce()
    {
        if (!IsHidden) return;
        if (ShellSurfaceOpen()) return; // leave Start, search and the notification centre alone
        foreach (var h in AllTaskbars())
            if (Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_HIDE);
    }

    /// <summary>True while Start, search, the quick settings or notification flyouts have focus.</summary>
    public static bool ShellSurfaceOpen()
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        var cls = Native.GetClass(fg);
        if (cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland" or "Xaml_WindowedPopupClass") return true;
        if (cls != "Windows.UI.Core.CoreWindow") return false;
        Native.GetWindowThreadProcessId(fg, out int pid);
        var exe = System.IO.Path.GetFileNameWithoutExtension(Native.GetProcessPath(pid) ?? "");
        return exe is "StartMenuExperienceHost" or "SearchHost" or "SearchApp" or "ShellExperienceHost" or "ShellHost";
    }

    public static void Restore()
    {
        _guard?.Stop();
        var s = Settings.Current;
        try
        {
            if (s.TaskbarOriginalState >= 0) SetState(s.TaskbarOriginalState);
            foreach (var h in AllTaskbars()) Native.ShowWindow(h, Native.SW_SHOW);
        }
        catch { }
        IsHidden = false;
        if (s.TaskbarOriginalState >= 0)
        {
            s.TaskbarOriginalState = -1;
            Settings.SaveNow();
        }
    }

    /// <summary>Called at start-up: if Perch closed without restoring the taskbar, put it back first.</summary>
    public static void RepairAfterCrash()
    {
        if (Settings.Current.TaskbarOriginalState >= 0) Restore();
    }
}
