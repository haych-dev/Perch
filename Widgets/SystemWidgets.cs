using System.IO;
using System.Windows;
using System.Windows.Controls;
using Perch.Core;
using Perch.Dock;

namespace Perch.Widgets;

public sealed class CpuWidget : Widget
{
    public override string Id => "cpu";
    public override string Name => "CPU";
    public override string Category => "System";
    public override string Description => "Processor load with a one-minute graph and the busiest apps.";
    public override double CardWidth => 320;

    Sparkline? _spark, _bigSpark;
    TextBlock? _value, _big;
    StackPanel? _top;
    int _topTick;

    protected override FrameworkElement BuildTile()
    {
        _spark = new Sparkline { Data = SystemSampler.Instance.Cpu, Width = 34 * Scale, Height = Size * 0.42, BrushKey = "Accent" };
        _value = TileText("0%", 13.5, FontWeights.SemiBold);
        var col = Ui.Col(0, TileText("CPU", 10, null, "Dock.Dim"), _value);
        col.Width = 36 * Scale;
        Tick();
        return Frame(Ui.Row(8 * Scale, _spark, col), null, 10);
    }

    public override void Tick()
    {
        var s = SystemSampler.Instance.Latest;
        if (_value != null) _value.Text = $"{s.Cpu:0}%";
        _spark?.Refresh();
        if (CardOpen)
        {
            if (_big != null) _big.Text = $"{s.Cpu:0}%";
            _bigSpark?.Refresh();
            if (++_topTick % 2 == 0) _ = LoadTop();
        }
    }

    public override FrameworkElement? BuildCard()
    {
        _big = Ui.Text("", 26, FontWeights.SemiBold);
        _bigSpark = new Sparkline { Data = SystemSampler.Instance.Cpu, Height = 70, BrushKey = "Accent" };
        _top = new StackPanel();
        _ = LoadTop();
        Tick();
        var head = Ui.Line(Ui.Col(0, Ui.Title("Processor"), Ui.Dim($"{Environment.ProcessorCount} logical processors", 11.5)), _big);
        return Ui.Col(12, head, _bigSpark, Ui.Dim("Busiest apps", 11.5), _top,
            Ui.Button("Task Manager", () => { Apps.Open("taskmgr.exe"); Host.CloseCard(); }));
    }

    async Task LoadTop()
    {
        var top = await SystemSampler.TopProcessesAsync(5);
        if (_top == null || !CardOpen) return;
        _top.Children.Clear();
        foreach (var (name, cpu, mem) in top)
            _top.Children.Add(Ui.Line(Ui.Text(name, 12.5), Ui.Text($"{cpu:0.0}%", 12.5, FontWeights.SemiBold)));
    }
}

public sealed class MemoryWidget : Widget
{
    public override string Id => "memory";
    public override string Name => "Memory";
    public override string Category => "System";
    public override string Description => "RAM in use, and which apps use the most.";

    RingGauge? _ring;
    TextBlock? _value;
    Grid? _bar;
    TextBlock? _detail;
    StackPanel? _top;

    protected override FrameworkElement BuildTile()
    {
        _ring = new RingGauge { Width = Size * 0.56, Height = Size * 0.56, Thickness = 3.5 * Scale, BrushKey = "Accent", LabelSize = 9 * Scale };
        _value = TileText("", 13.5, FontWeights.SemiBold);
        var col = Ui.Col(0, TileText("RAM", 10, null, "Dock.Dim"), _value);
        col.MinWidth = 44 * Scale;
        Tick();
        return Frame(Ui.Row(8 * Scale, _ring, col), null, 10);
    }

    public override void Tick()
    {
        var s = SystemSampler.Instance.Latest;
        if (_ring != null) _ring.Value = s.MemPct;
        if (_value != null) _value.Text = $"{s.MemUsed / 1073741824.0:0.0} GB";
        if (CardOpen && _bar != null && _detail != null)
        {
            Ui.SetBar(_bar, s.MemPct);
            _detail.Text = $"{Fmt.Bytes(s.MemUsed)} of {Fmt.Bytes(s.MemTotal)} in use ({s.MemPct:0}%)";
        }
    }

    public override FrameworkElement? BuildCard()
    {
        _bar = Ui.Bar(SystemSampler.Instance.Latest.MemPct, "Accent", 8);
        _detail = Ui.Dim("", 12);
        _top = new StackPanel();
        Tick();
        _ = Load();
        return Ui.Col(12, Ui.Title("Memory"), _bar, _detail, Ui.Divider(), Ui.Dim("Using the most", 11.5), _top);
    }

    async Task Load()
    {
        var top = await SystemSampler.TopMemoryAsync(6);
        if (_top == null) return;
        _top.Children.Clear();
        foreach (var (name, mem) in top) _top.Children.Add(Ui.Line(Ui.Text(name, 12.5), Ui.Text(Fmt.Bytes(mem), 12.5, FontWeights.SemiBold)));
    }
}

public sealed class NetworkWidget : Widget
{
    public override string Id => "network";
    public override string Name => "Network";
    public override string Category => "System";
    public override string Description => "Download and upload speed right now.";

    TextBlock? _down, _up, _bigDown, _bigUp;
    Sparkline? _sd, _su;

    protected override FrameworkElement BuildTile()
    {
        _down = TileText("", 12, FontWeights.SemiBold);
        _up = TileText("", 12, null, "Dock.Dim");
        var col = Ui.Col(0, Ui.Row(4, TileGlyph("\uE74B", 10), _down), Ui.Row(4, TileGlyph("\uE74A", 10, "Dock.Dim"), _up));
        col.Width = 54 * Scale;
        Tick();
        return Frame(col, null, 10);
    }

    public override void Tick()
    {
        var s = SystemSampler.Instance.Latest;
        if (_down != null) _down.Text = Fmt.Rate(s.NetDown);
        if (_up != null) _up.Text = Fmt.Rate(s.NetUp);
        if (CardOpen)
        {
            if (_bigDown != null) _bigDown.Text = Fmt.Bytes(s.NetDown) + "/s";
            if (_bigUp != null) _bigUp.Text = Fmt.Bytes(s.NetUp) + "/s";
            _sd?.Refresh();
            _su?.Refresh();
        }
    }

    public override FrameworkElement? BuildCard()
    {
        _bigDown = Ui.Text("", 15, FontWeights.SemiBold);
        _bigUp = Ui.Text("", 15, FontWeights.SemiBold);
        _sd = new Sparkline { Data = SystemSampler.Instance.Down, AutoScale = true, Height = 50, BrushKey = "Good" };
        _su = new Sparkline { Data = SystemSampler.Instance.Up, AutoScale = true, Height = 50, BrushKey = "Warn" };
        Tick();
        return Ui.Col(10, Ui.Title("Network"),
            Ui.Line(Ui.Dim("Download"), _bigDown), _sd,
            Ui.Line(Ui.Dim("Upload"), _bigUp), _su,
            Ui.Button("Network settings", () => { QuickSettings.OpenSettings("network-status"); Host.CloseCard(); }));
    }
}

public sealed class StorageWidget : Widget
{
    public override string Id => "storage";
    public override string Name => "Storage";
    public override string Category => "System";
    public override string Description => "Free space on each drive.";

    RingGauge? _ring;
    TextBlock? _free;

    protected override FrameworkElement BuildTile()
    {
        _ring = new RingGauge { Width = Size * 0.56, Height = Size * 0.56, Thickness = 3.5 * Scale, BrushKey = "Warn" };
        _free = TileText("", 12.5, FontWeights.SemiBold);
        Tick();
        return Frame(Ui.Row(8 * Scale, _ring, Ui.Col(0, TileText("Free", 10, null, "Dock.Dim"), _free)), null, 10);
    }

    public override void Tick()
    {
        var sys = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var d = SystemSampler.Instance.Latest.Drives.FirstOrDefault(x => x.Name.Equals(sys, StringComparison.OrdinalIgnoreCase));
        if (d.Total <= 0 || _ring == null || _free == null) return;
        _ring.Value = 100.0 * (d.Total - d.Free) / d.Total;
        _ring.BrushKey = d.Free < d.Total * 0.1 ? "Bad" : "Warn";
        _free.Text = Fmt.Bytes(d.Free, 0);
    }

    public override FrameworkElement? BuildCard()
    {
        var col = Ui.Col(12, Ui.Title("Storage"));
        foreach (var d in SystemSampler.Instance.Latest.Drives)
        {
            double pct = d.Total > 0 ? 100.0 * (d.Total - d.Free) / d.Total : 0;
            var name = $"{(d.Label.Length > 0 ? d.Label : "Local Disk")} ({d.Name.TrimEnd('\\')})";
            var path = d.Name;
            var row = Ui.Col(5, Ui.Line(Ui.Text(name, 13), Ui.Dim($"{Fmt.Bytes(d.Free)} free")), Ui.Bar(pct, pct > 90 ? "Bad" : "Accent"), Ui.Dim($"{Fmt.Bytes(d.Total - d.Free)} of {Fmt.Bytes(d.Total)} used", 11));
            col.Children.Add(Ui.ListRow(row, () => { Apps.Open(path); Host.CloseCard(); }));
        }
        col.Children.Add(Ui.Button("Storage settings", () => { QuickSettings.OpenSettings("storagesense"); Host.CloseCard(); }));
        return col;
    }
}

public sealed class BatteryWidget : Widget
{
    public override string Id => "battery";
    public override string Name => "Battery";
    public override string Category => "System";
    public override string Description => "Charge level and time left (laptops).";

    BatteryIcon? _icon;
    TextBlock? _pct;

    protected override FrameworkElement BuildTile()
    {
        _icon = new BatteryIcon { Width = 26 * Scale, Height = 13 * Scale };
        _pct = TileText("", 13, FontWeights.SemiBold);
        Tick();
        return Frame(Ui.Row(7 * Scale, _icon, _pct), null, 10);
    }

    public override void Tick()
    {
        var s = SystemSampler.Instance.Latest;
        if (_icon == null || _pct == null) return;
        if (!s.HasBattery) { _pct.Text = "AC"; _icon.Percent = 100; _icon.Charging = true; return; }
        _icon.Percent = s.BatteryPct;
        _icon.Charging = s.Charging || s.OnAc;
        _pct.Text = $"{s.BatteryPct}%";
    }

    public override FrameworkElement? BuildCard()
    {
        var s = SystemSampler.Instance.Latest;
        string status = !s.HasBattery ? "This PC runs on mains power; there's no battery."
            : s.Charging ? "Charging"
            : s.OnAc ? "Plugged in"
            : s.BatteryLeft is { } left ? $"{(int)left.TotalHours} h {left.Minutes} min left" : "On battery";
        return Ui.Col(12,
            Ui.Line(Ui.Title("Battery"), Ui.Text(s.HasBattery ? $"{s.BatteryPct}%" : "", 20, FontWeights.SemiBold)),
            s.HasBattery ? Ui.Bar(s.BatteryPct, s.BatteryPct <= 20 ? "Bad" : "Good", 8) : new Border(),
            Ui.Wrap(Ui.Dim(status, 12.5)),
            Ui.Button("Power & battery settings", () => { QuickSettings.OpenSettings("powersleep"); Host.CloseCard(); }));
    }
}
