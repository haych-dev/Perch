using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Perch.Core;

public sealed record GpuReading(string Name, double Load, double? Temp, double? MemUsedMb, double? MemTotalMb);

/// <summary>
/// GPU load and temperature. NVIDIA cards report through nvidia-smi (installed with the driver), which is kept
/// running in its own loop so nothing is started every few seconds. Other cards, and NVIDIA cards while nvidia-smi
/// isn't running, fall back to Windows' "GPU Engine" counters, which give load but not temperature.
/// Only runs while a GPU tile is on the dock.
/// </summary>
public sealed class Gpu
{
    public static Gpu Instance { get; } = new();
    public event Action? Changed;
    public GpuReading? Latest { get; private set; }
    public Ring Load { get; } = new(60);

    readonly object _gate = new();
    Process? _smi;
    Timer? _counterTimer, _retryTimer;
    Dispatcher? _ui;
    int _users, _smiRestarts;

    public void Start(Dispatcher ui)
    {
        _ui = ui;
        lock (_gate)
        {
            if (_users++ > 0) return;
            _smiRestarts = 0;
            if (!StartSmi()) StartCounters();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (--_users > 0) return;
            _users = 0;
            var smi = _smi;
            _smi = null;
            try { if (smi is { HasExited: false }) smi.Kill(); } catch { }
            _retryTimer?.Dispose();
            _retryTimer = null;
            StopCounters();
        }
    }

    static string? FindSmi()
    {
        foreach (var p in new[]
        {
            Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
        })
            if (File.Exists(p)) return p;
        return null;
    }

    static bool _exitHooked;

    /// <summary>Starts nvidia-smi's own two-second loop. Call with _gate held.</summary>
    bool StartSmi()
    {
        var exe = FindSmi();
        if (exe == null) return false;
        try
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo(exe, "--query-gpu=name,utilization.gpu,temperature.gpu,memory.used,memory.total --format=csv,noheader,nounits -lms 2000")
                { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true },
                EnableRaisingEvents = true,
            };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) Parse(e.Data); };
            p.Exited += (_, _) => OnSmiExited(p);
            p.Start();
            p.BeginOutputReadLine();
            _smi = p;
            if (!_exitHooked)
            {
                _exitHooked = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { if (_smi is { HasExited: false } s) s.Kill(); } catch { } };
            }
            return true;
        }
        catch (Exception ex) { Log.Error(ex, "nvidia-smi could not be started"); return false; }
    }

    /// <summary>
    /// nvidia-smi can exit on its own (a driver update or a GPU reset). Keep the tile going with the Windows
    /// counters and try nvidia-smi again a little later, so temperature comes back.
    /// </summary>
    void OnSmiExited(Process p)
    {
        lock (_gate)
        {
            if (_smi != p) return; // stopped on purpose
            _smi = null;
            if (_users == 0) return;
            StartCounters();
            _retryTimer?.Dispose();
            if (_smiRestarts++ < 5)
                _retryTimer = new Timer(_ =>
                {
                    lock (_gate)
                    {
                        if (_users == 0 || _smi != null) return;
                        if (StartSmi()) StopCounters();
                    }
                }, null, 30_000 * _smiRestarts, Timeout.Infinite);
        }
    }

    void Parse(string line)
    {
        var f = line.Split(',');
        if (f.Length < 5) return;
        static double? Num(string s) => double.TryParse(s.AsSpan().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        Publish(new GpuReading(f[0].Trim(), Num(f[1]) ?? 0, Num(f[2]), Num(f[3]), Num(f[4])));
    }

    // ───────────── Windows "GPU Engine" counters, read through one PDH query ─────────────
    // One wildcard query reads every 3D engine in a single call. (PerformanceCounter objects, one per engine,
    // each re-read the whole counter set: over 100 ms of CPU and tens of MB of garbage every two seconds.)

    readonly object _pdh = new();
    IntPtr _query, _counter, _buf;
    uint _bufSize;

    /// <summary>Call with _gate held.</summary>
    void StartCounters()
    {
        if (_counterTimer != null) return;
        try
        {
            if (_query == IntPtr.Zero)
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) { _query = IntPtr.Zero; return; }
                if (PdhAddEnglishCounter(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _counter) != 0)
                {
                    PdhCloseQuery(_query);
                    _query = IntPtr.Zero;
                    return;
                }
            }
            PdhCollectQueryData(_query); // the first sample only primes the rate
            _counterTimer = new Timer(_ => SampleCounters(), null, 2000, 2000);
        }
        catch (Exception ex) { Log.Error(ex, "GPU counters could not be opened"); }
    }

    /// <summary>Call with _gate held.</summary>
    void StopCounters()
    {
        _counterTimer?.Dispose();
        _counterTimer = null;
        lock (_pdh) // a sample in progress finishes before the query is closed under it
        {
            if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; _counter = IntPtr.Zero; }
            if (_buf != IntPtr.Zero) { Marshal.FreeHGlobal(_buf); _buf = IntPtr.Zero; _bufSize = 0; }
        }
    }

    void SampleCounters()
    {
        if (!Monitor.TryEnter(_pdh)) return; // the previous sample is still running
        try
        {
            if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != 0) return;
            uint size = _bufSize;
            int r = PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out uint count, _buf);
            if (r == PDH_MORE_DATA)
            {
                if (_buf != IntPtr.Zero) Marshal.FreeHGlobal(_buf);
                _bufSize = size + 4096; // room for a few more engines before growing again
                _buf = Marshal.AllocHGlobal((int)_bufSize);
                size = _bufSize;
                r = PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, _buf);
            }
            if (r != 0) return;
            double total = 0;
            for (int i = 0; i < count; i++)
            {
                // PDH_FMT_COUNTERVALUE_ITEM_W: name pointer, then { CStatus, (padding), double }.
                var item = _buf + i * 24;
                if (Marshal.ReadInt32(item, 8) == 0) total += BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
            }
            Publish(new GpuReading("GPU", Math.Clamp(total, 0, 100), null, null, null));
        }
        catch (Exception ex) { Log.Error(ex, "GPU counters could not be read"); }
        finally { Monitor.Exit(_pdh); }
    }

    void Publish(GpuReading r) => _ui?.BeginInvoke(() =>
    {
        Latest = r;
        Load.Push(r.Load);
        Changed?.Invoke();
    });

    const uint PDH_FMT_DOUBLE = 0x200, PDH_FMT_NOCAP100 = 0x8000;
    const int PDH_MORE_DATA = unchecked((int)0x800007D2);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhOpenQueryW")] static extern int PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")] static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")] static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr query);
}
