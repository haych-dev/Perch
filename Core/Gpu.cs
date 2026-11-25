using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Threading;

namespace Perch.Core;

public sealed record GpuReading(string Name, double Load, double? Temp, double? MemUsedMb, double? MemTotalMb);

/// <summary>
/// GPU load and temperature. NVIDIA cards report through nvidia-smi (installed with the driver), which is kept
/// running in its own loop so nothing is started every few seconds. Other cards fall back to Windows' "GPU Engine"
/// counters, which give load but not temperature. Only runs while a GPU tile is on the dock.
/// </summary>
public sealed class Gpu
{
    public static Gpu Instance { get; } = new();
    public event Action? Changed;
    public GpuReading? Latest { get; private set; }
    public Ring Load { get; } = new(60);

    Process? _smi;
    Timer? _counterTimer;
    Dispatcher? _ui;
    int _users;

    public void Start(Dispatcher ui)
    {
        _ui = ui;
        if (_users++ > 0) return;
        if (!StartSmi()) _counterTimer = new Timer(_ => SampleCounters(), null, 0, 2000);
    }

    public void Stop()
    {
        if (--_users > 0) return;
        _users = 0;
        try { if (_smi is { HasExited: false }) _smi.Kill(); } catch { }
        _smi = null;
        _counterTimer?.Dispose();
        _counterTimer = null;
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
            p.Exited += (_, _) => { if (_smi == p && _users > 0) { _smi = null; _counterTimer ??= new Timer(_ => SampleCounters(), null, 0, 2000); } };
            p.Start();
            p.BeginOutputReadLine();
            _smi = p;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { if (!p.HasExited) p.Kill(); } catch { } };
            return true;
        }
        catch (Exception ex) { Log.Error(ex, "nvidia-smi could not be started"); return false; }
    }

    void Parse(string line)
    {
        var f = line.Split(',').Select(x => x.Trim()).ToArray();
        if (f.Length < 5) return;
        double? Num(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        Publish(new GpuReading(f[0], Num(f[1]) ?? 0, Num(f[2]), Num(f[3]), Num(f[4])));
    }

    PerformanceCounterCategory? _engines;
    Dictionary<string, PerformanceCounter> _counters = [];

    void SampleCounters()
    {
        try
        {
            _engines ??= new PerformanceCounterCategory("GPU Engine");
            var names = _engines.GetInstanceNames().Where(n => n.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)).ToHashSet();
            foreach (var gone in _counters.Keys.Where(k => !names.Contains(k)).ToList()) { _counters[gone].Dispose(); _counters.Remove(gone); }
            double total = 0;
            foreach (var n in names)
            {
                if (!_counters.TryGetValue(n, out var c)) { c = new PerformanceCounter("GPU Engine", "Utilization Percentage", n, true); c.NextValue(); _counters[n] = c; continue; }
                total += c.NextValue();
            }
            Publish(new GpuReading("GPU", Math.Clamp(total, 0, 100), null, null, null));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GPU counters could not be read");
            _counterTimer?.Dispose();
            _counterTimer = null;
        }
    }

    void Publish(GpuReading r) => _ui?.BeginInvoke(() =>
    {
        Latest = r;
        Load.Push(r.Load);
        Changed?.Invoke();
    });
}
