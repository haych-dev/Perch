using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Perch.Core;

public sealed class SystemSample
{
    public double Cpu;
    public ulong MemTotal, MemUsed;
    public double MemPct => MemTotal > 0 ? 100.0 * MemUsed / MemTotal : 0;
    public double NetDown, NetUp;
    public bool HasBattery, Charging, OnAc;
    public int BatteryPct = -1;
    public TimeSpan? BatteryLeft;
    public List<(string Name, string Label, long Total, long Free)> Drives = [];
}

/// <summary>Samples CPU, memory, network and battery once a second off the UI thread.</summary>
public sealed class SystemSampler
{
    public static SystemSampler Instance { get; } = new();
    public event Action<SystemSample>? Updated;
    public SystemSample Latest { get; private set; } = new();
    public Ring Cpu { get; } = new(60);
    public Ring Mem { get; } = new(60);
    public Ring Down { get; } = new(60);
    public Ring Up { get; } = new(60);

    long[] _prevIdle = [], _prevTotal = [];
    IntPtr _buf;
    long _prevRx, _prevTx;
    bool _netPrimed;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    long _lastMs;
    int _tick;
    List<(string, string, long, long)> _drives = [];
    Timer? _timer;
    Dispatcher? _ui;

    NetworkInterface[]? _nics;
    bool _nicsStale;

    public void Start(Dispatcher ui)
    {
        _ui = ui;
        NetworkChange.NetworkAddressChanged += (_, _) => _nicsStale = true;
        NetworkChange.NetworkAvailabilityChanged += (_, _) => _nicsStale = true;
        _timer = new Timer(_ => Tick(), null, 0, 1000);
    }

    void Tick()
    {
        try
        {
            var s = Sample();
            _ui?.BeginInvoke(() =>
            {
                Latest = s;
                Cpu.Push(s.Cpu);
                Mem.Push(s.MemPct);
                Down.Push(s.NetDown);
                Up.Push(s.NetUp);
                Updated?.Invoke(s);
            });
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    SystemSample Sample()
    {
        long now = _clock.ElapsedMilliseconds;
        double dt = Math.Max(0.1, (now - _lastMs) / 1000.0);
        _lastMs = now;
        var s = new SystemSample { Cpu = SampleCpu() };

        var mem = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        if (Native.GlobalMemoryStatusEx(ref mem)) { s.MemTotal = mem.ullTotalPhys; s.MemUsed = mem.ullTotalPhys - mem.ullAvailPhys; }

        try
        {
            // Listing the adapters is the expensive part (about 12 ms), so the list is kept and only
            // rebuilt when Windows reports a network change, or once a minute.
            if (_nics == null || _tick % 60 == 0 || Volatile.Read(ref _nicsStale))
            {
                _nicsStale = false;
                var nics = NetworkInterface.GetAllNetworkInterfaces().Where(nic =>
                    nic.OperationalStatus == OperationalStatus.Up
                    && nic.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    && !nic.Description.Contains("Filter", StringComparison.OrdinalIgnoreCase)
                    && !nic.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)).ToArray();
                // A different set of adapters gives totals that can't be compared with the last ones.
                if (_nics == null || !nics.Select(n => n.Id).SequenceEqual(_nics.Select(n => n.Id))) _netPrimed = false;
                _nics = nics;
            }
            long rx = 0, tx = 0;
            foreach (var nic in _nics)
            {
                var st = nic.GetIPStatistics();
                rx += st.BytesReceived; tx += st.BytesSent;
            }
            if (_netPrimed) { s.NetDown = Math.Max(0, (rx - _prevRx) / dt); s.NetUp = Math.Max(0, (tx - _prevTx) / dt); }
            _prevRx = rx; _prevTx = tx; _netPrimed = true;
        }
        catch { _nicsStale = true; }

        if (Native.GetSystemPowerStatus(out var ps))
        {
            s.HasBattery = ps.BatteryFlag != 128 && ps.BatteryFlag != 255;
            s.OnAc = ps.ACLineStatus == 1;
            s.Charging = (ps.BatteryFlag & 8) != 0;
            s.BatteryPct = ps.BatteryLifePercent <= 100 ? ps.BatteryLifePercent : -1;
            if (ps.BatteryLifeTime > 0) s.BatteryLeft = TimeSpan.FromSeconds(ps.BatteryLifeTime);
        }

        if (_tick++ % 30 == 0)
        {
            var list = new List<(string, string, long, long)>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try { if (d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable) list.Add((d.Name, d.VolumeLabel, d.TotalSize, d.TotalFreeSpace)); }
                catch { }
            }
            _drives = list;
        }
        s.Drives = _drives;
        return s;
    }

    double SampleCpu()
    {
        int n = Environment.ProcessorCount;
        const int entry = 48; // SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
        if (_buf == IntPtr.Zero) { _buf = Marshal.AllocHGlobal(entry * n); _prevIdle = new long[n]; _prevTotal = new long[n]; }
        if (Native.NtQuerySystemInformation(Native.SystemProcessorPerformanceInformation, _buf, entry * n, out int ret) != 0) return 0;
        long sumIdle = 0, sumTotal = 0;
        for (int i = 0; i < Math.Min(n, ret / entry); i++)
        {
            long idle = Marshal.ReadInt64(_buf, i * entry), kernel = Marshal.ReadInt64(_buf, i * entry + 8), user = Marshal.ReadInt64(_buf, i * entry + 16);
            long total = kernel + user;
            sumIdle += idle - _prevIdle[i]; sumTotal += total - _prevTotal[i];
            _prevIdle[i] = idle; _prevTotal[i] = total;
        }
        return sumTotal > 0 ? Math.Clamp(100.0 * (sumTotal - sumIdle) / sumTotal, 0, 100) : 0;
    }

    /// <summary>Busiest processes by CPU over roughly the last second (only computed while a card asks for it).</summary>
    public static async Task<List<(string Name, double Cpu, long Mem)>> TopProcessesAsync(int count)
    {
        return await Task.Run(async () =>
        {
            var first = new Dictionary<int, TimeSpan>();
            foreach (var p in Process.GetProcesses()) { try { first[p.Id] = p.TotalProcessorTime; } catch { } finally { p.Dispose(); } }
            var sw = Stopwatch.StartNew();
            await Task.Delay(700);
            double elapsed = sw.Elapsed.TotalMilliseconds * Environment.ProcessorCount;
            var byName = new Dictionary<string, (double Cpu, long Mem)>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    double cpu = first.TryGetValue(p.Id, out var t0) ? (p.TotalProcessorTime - t0).TotalMilliseconds / elapsed * 100 : 0;
                    long mem = p.WorkingSet64;
                    byName.TryGetValue(p.ProcessName, out var acc);
                    byName[p.ProcessName] = (acc.Cpu + cpu, acc.Mem + mem);
                }
                catch { }
                finally { p.Dispose(); }
            }
            byName.Remove("Idle");
            return byName.Select(kv => (kv.Key, Math.Max(0, kv.Value.Cpu), kv.Value.Mem)).OrderByDescending(x => x.Item2).ThenByDescending(x => x.Item3).Take(count).ToList();
        });
    }

    public static async Task<List<(string Name, long Mem)>> TopMemoryAsync(int count) => await Task.Run(() =>
    {
        var byName = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { byName[p.ProcessName] = byName.GetValueOrDefault(p.ProcessName) + p.PrivateMemorySize64; } catch { } finally { p.Dispose(); }
        }
        return byName.OrderByDescending(kv => kv.Value).Take(count).Select(kv => (kv.Key, kv.Value)).ToList();
    });
}

public sealed class Ring(int size)
{
    readonly double[] _v = new double[size];
    int _start, _count;
    public int Count => _count;
    public int Capacity => size;
    public double this[int i] => _v[(_start + i) % size];
    public double Max
    {
        get
        {
            double m = _count == 0 ? 0 : double.MinValue;
            for (int i = 0; i < _count; i++) m = Math.Max(m, this[i]);
            return m;
        }
    }
    public void Push(double x)
    {
        if (_count < size) _v[(_start + _count++) % size] = x;
        else { _v[_start] = x; _start = (_start + 1) % size; }
    }
}

public static class Fmt
{
    static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];
    public static string Bytes(double b, int dec = 1)
    {
        int u = 0;
        while (b >= 1024 && u < Units.Length - 1) { b /= 1024; u++; }
        return b.ToString(u == 0 ? "0" : (b >= 100 ? "0" : "0." + new string('0', dec))) + " " + Units[u];
    }
    public static string Rate(double b)
    {
        // Short form for tiles: "1.2M", "340K"
        if (b < 1024) return $"{b:0}B";
        if (b < 1024 * 1024) return $"{b / 1024:0}K";
        if (b < 1024 * 1024 * 1024) return $"{b / 1048576:0.0}M";
        return $"{b / 1073741824:0.0}G";
    }
}
