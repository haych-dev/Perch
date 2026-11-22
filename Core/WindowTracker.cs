using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;

namespace Perch.Core;

public sealed class AppWindow
{
    public IntPtr Hwnd;
    public int Pid;
    public string Title = "";
    public string? ExePath;
    public string? AppId;
    public bool Minimized;
}

/// <summary>Windows that belong to one app, the way the taskbar groups them.</summary>
public sealed class AppGroup
{
    public string Key = "";
    public string Name = "";
    public string? ExePath;
    public string? AppId;
    public List<AppWindow> Windows = [];

    public string? IconSource => IconFor(ExePath, AppId);

    public static string? IconFor(string? exe, string? appId)
    {
        bool packaged = exe == null || exe.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(exe).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
        if (packaged && appId != null) return "shell:AppsFolder\\" + appId;
        return exe;
    }
}

/// <summary>Keeps a live list of app windows (what the taskbar would show) and the last window the user worked in.</summary>
public sealed class WindowTracker
{
    public static WindowTracker Instance { get; } = new();

    public event Action? Changed;
    public IReadOnlyList<AppGroup> Groups => _groups;
    /// <summary>The most recent foreground window that isn't Perch's own.</summary>
    public IntPtr LastActive { get; private set; }

    List<AppGroup> _groups = [];
    readonly Dictionary<IntPtr, (int Pid, string? Exe, string? AppId)> _info = [];
    readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    readonly int _ownPid = Environment.ProcessId;
    DispatcherTimer? _timer, _fgTimer;
    string _signature = "";
    Native.WinEventDelegate? _hookProc;
    IntPtr _hook;

    public void Start()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(900) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        _fgTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _fgTimer.Tick += (_, _) => TrackForeground();
        _fgTimer.Start();

        // Window creation / destruction / title changes trigger an early refresh so the dock feels immediate.
        // Hook callbacks can arrive while the UI thread is inside another call (re-entrancy),
        // so they only schedule work.
        var ui = Dispatcher.CurrentDispatcher;
        _hookProc = (_, ev, hwnd, idObject, _, _, _) =>
        {
            if (idObject != 0) return; // OBJID_WINDOW only
            if (ev == Native.EVENT_SYSTEM_FOREGROUND) { ui.BeginInvoke(TrackForeground); return; }
            _timer?.Stop();
            _timer!.Interval = TimeSpan.FromMilliseconds(120);
            _timer.Start();
        };
        _hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _hookProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        Native.SetWinEventHook(Native.EVENT_OBJECT_DESTROY, Native.EVENT_OBJECT_HIDE, IntPtr.Zero, _hookProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        Native.SetWinEventHook(Native.EVENT_SYSTEM_MINIMIZESTART, Native.EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, _hookProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        Native.SetWinEventHook(Native.EVENT_OBJECT_CLOAKED, Native.EVENT_OBJECT_UNCLOAKED, IntPtr.Zero, _hookProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        Refresh();
    }

    void TrackForeground()
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return;
        Native.GetWindowThreadProcessId(fg, out int pid);
        if (pid == _ownPid) return;
        if (fg != LastActive)
        {
            LastActive = fg;
            Changed?.Invoke();
        }
    }

    public void Refresh()
    {
        if (_timer != null) { _timer.Stop(); _timer.Interval = TimeSpan.FromMilliseconds(900); _timer.Start(); }

        var windows = new List<AppWindow>();
        var seen = new HashSet<IntPtr>();
        Native.EnumWindows((h, _) =>
        {
            if (IsAppWindow(h))
            {
                seen.Add(h);
                if (!_info.TryGetValue(h, out var info))
                {
                    Native.GetWindowThreadProcessId(h, out int pid);
                    info = (pid, Native.GetProcessPath(pid), Native.GetWindowAppId(h));
                    _info[h] = info;
                }
                if (info.Pid != _ownPid)
                    windows.Add(new AppWindow { Hwnd = h, Pid = info.Pid, ExePath = info.Exe, AppId = info.AppId, Title = Native.GetText(h), Minimized = Native.IsIconic(h) });
            }
            return true;
        }, IntPtr.Zero);
        foreach (var dead in _info.Keys.Where(k => !seen.Contains(k)).ToList()) _info.Remove(dead);

        var groups = new List<AppGroup>();
        foreach (var w in windows)
        {
            string key = KeyFor(w.ExePath, w.AppId);
            var g = groups.FirstOrDefault(x => x.Key == key);
            if (g == null)
            {
                g = new AppGroup { Key = key, ExePath = w.ExePath, AppId = w.AppId, Name = NameFor(w) };
                groups.Add(g);
            }
            g.Windows.Add(w);
        }

        // Keep the existing order so icons don't jump around; new apps go at the end.
        var ordered = new List<AppGroup>();
        foreach (var old in _groups) { var g = groups.FirstOrDefault(x => x.Key == old.Key); if (g != null) ordered.Add(g); }
        ordered.AddRange(groups.Where(g => !ordered.Contains(g)));

        string sig = string.Join("|", ordered.Select(g => g.Key + ":" + string.Join(",", g.Windows.Select(w => $"{w.Hwnd}{(w.Minimized ? "m" : "")}{w.Title}"))));
        _groups = ordered;
        if (sig != _signature)
        {
            _signature = sig;
            Changed?.Invoke();
        }
    }

    public static string KeyFor(string? exe, string? appId) => (appId ?? exe ?? "?").ToLowerInvariant();

    string NameFor(AppWindow w)
    {
        string key = KeyFor(w.ExePath, w.AppId);
        if (_names.TryGetValue(key, out var n)) return n;
        string name = "";
        try
        {
            bool frameHost = w.ExePath != null && Path.GetFileName(w.ExePath).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
            if (w.ExePath != null && !frameHost)
            {
                var vi = FileVersionInfo.GetVersionInfo(w.ExePath);
                name = vi.FileDescription?.Trim() ?? "";
                if (name.Length == 0 || name.Length > 40) name = vi.ProductName?.Trim() ?? "";
                if (name.Length == 0) name = Path.GetFileNameWithoutExtension(w.ExePath);
            }
        }
        catch { }
        if (name.Length == 0) name = w.Title;
        if (name.Length > 0) _names[key] = name;
        return name;
    }

    static bool IsAppWindow(IntPtr h)
    {
        if (!Native.IsWindowVisible(h)) return false;
        long ex = Native.ExStyle(h);
        bool appWindow = (ex & Native.WS_EX_APPWINDOW) != 0;
        if (!appWindow)
        {
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0 || (ex & Native.WS_EX_NOACTIVATE) != 0) return false;
            if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return false;
        }
        if (Native.IsCloaked(h)) return false;
        if (Native.GetWindowTextLength(h) == 0) return false;
        var cls = Native.GetClass(h);
        return cls is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow");
    }

    // ───────────── Actions ─────────────

    /// <summary>What a single click on an app icon does: restore / focus, or minimise when it is already in front.</summary>
    public void Toggle(AppGroup g)
    {
        if (g.Windows.Count == 0) return;
        var active = g.Windows.FirstOrDefault(w => w.Hwnd == LastActive && !Native.IsIconic(w.Hwnd));
        if (active != null && Native.GetForegroundWindow() is var fg && (fg == active.Hwnd || IsOwnWindow(fg)))
        {
            Native.ShowWindowAsync(active.Hwnd, Native.SW_MINIMIZE);
            LastActive = IntPtr.Zero;
            return;
        }
        // Bring every window of the app forward, the last-used one on top.
        var target = g.Windows.FirstOrDefault(w => w.Hwnd == LastActive) ?? g.Windows[0];
        Activate(target.Hwnd);
    }

    bool IsOwnWindow(IntPtr h)
    {
        Native.GetWindowThreadProcessId(h, out int pid);
        return pid == _ownPid;
    }

    public static void Activate(IntPtr hwnd)
    {
        if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
        Native.SetForegroundWindow(hwnd);
    }

    public static void Close(IntPtr hwnd) => Native.PostMessage(hwnd, Native.WM_SYSCOMMAND, new IntPtr(Native.SC_CLOSE), IntPtr.Zero);

    public static void EndTask(AppGroup g)
    {
        foreach (var pid in g.Windows.Select(w => w.Pid).Distinct())
        {
            try { using var p = Process.GetProcessById(pid); p.Kill(); } catch { }
        }
    }
}
