using System.Runtime.InteropServices;

namespace Perch.Core;

public sealed record MonitorInfo(IntPtr Handle, Native.RECT Bounds, Native.RECT Work, bool IsPrimary, string Device, double Scale)
{
    public string Label(int index) => $"Display {index + 1}{(IsPrimary ? " (main)" : "")} · {Bounds.Width}×{Bounds.Height}";
}

public static class Monitors
{
    /// <summary>All displays, primary first, then left to right.</summary>
    public static List<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr _, ref Native.RECT _, IntPtr _) =>
        {
            var mi = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>() };
            if (Native.GetMonitorInfo(m, ref mi))
            {
                double scale = 1;
                if (Native.GetDpiForMonitor(m, 0, out uint dx, out _) == 0 && dx > 0) scale = dx / 96.0;
                list.Add(new MonitorInfo(m, mi.rcMonitor, mi.rcWork, (mi.dwFlags & 1) != 0, mi.szDevice, scale));
            }
            return true;
        }, IntPtr.Zero);
        return list.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
    }

    public static MonitorInfo Pick(int index)
    {
        var all = All();
        if (all.Count == 0) return new MonitorInfo(IntPtr.Zero, new Native.RECT(0, 0, 1920, 1080), new Native.RECT(0, 0, 1920, 1040), true, "", 1);
        return index >= 0 && index < all.Count ? all[index] : all[0];
    }
}
