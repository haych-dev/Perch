using System.Runtime.InteropServices;

namespace Perch.Core;

/// <summary>
/// Screen brightness for external monitors (DDC/CI, the same channel the monitor's own buttons use) and for
/// built-in laptop screens (WMI). Talking to a monitor takes tens of milliseconds, so call off the UI thread.
/// </summary>
public static class Brightness
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
    }

    [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);
    [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint size, [Out] PHYSICAL_MONITOR[] monitors);
    [DllImport("dxva2.dll", SetLastError = true)] static extern bool DestroyPhysicalMonitors(uint size, [In] PHYSICAL_MONITOR[] monitors);
    [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetMonitorBrightness(IntPtr hMonitor, out uint min, out uint current, out uint max);
    [DllImport("dxva2.dll", SetLastError = true)] static extern bool SetMonitorBrightness(IntPtr hMonitor, uint value);

    /// <summary>Calls the action for each physical monitor that answers DDC/CI brightness requests.</summary>
    static void ForEachDdc(Action<IntPtr, uint, uint, uint> action)
    {
        foreach (var m in Monitors.All())
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(m.Handle, out uint n) || n == 0) continue;
            var arr = new PHYSICAL_MONITOR[n];
            if (!GetPhysicalMonitorsFromHMONITOR(m.Handle, n, arr)) continue;
            try
            {
                foreach (var pm in arr)
                    if (GetMonitorBrightness(pm.hPhysicalMonitor, out uint min, out uint cur, out uint max) && max > min)
                        action(pm.hPhysicalMonitor, min, cur, max);
            }
            finally { DestroyPhysicalMonitors(n, arr); }
        }
    }

    /// <summary>Brightness of the first screen that reports it, 0–100, or -1 if none can be controlled.</summary>
    public static int Get()
    {
        int result = -1;
        try { ForEachDdc((_, min, cur, max) => { if (result < 0) result = (int)Math.Round(100.0 * (cur - min) / (max - min)); }); } catch { }
        if (result < 0) result = QuickSettings.GetBrightness();
        return result;
    }

    /// <summary>Sets every controllable screen to the same level (0–100).</summary>
    public static void Set(int pct)
    {
        pct = Math.Clamp(pct, 0, 100);
        bool any = false;
        try { ForEachDdc((h, min, _, max) => { any = true; SetMonitorBrightness(h, (uint)Math.Round(min + (max - min) * pct / 100.0)); }); } catch { }
        if (!any) QuickSettings.SetBrightness(pct);
    }
}
