using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Perch.Core;
using Perch.Dock;

namespace Perch.Widgets;

/// <summary>The app using the most processor right now.</summary>
public sealed class TopCpuWidget : Widget
{
    public override string Id => "topcpu";
    public override string Name => "Top CPU";
    public override string Category => "System";
    public override string Description => "Which app is using the most processor right now.";
    TextBlock? _name, _value;
    StackPanel? _list;
    List<(string Name, double Cpu, long Mem)> _last = [];
    int _tick;
    bool _busy;

    protected override FrameworkElement BuildTile()
    {
        _name = TileText("…", 11.5, FontWeights.SemiBold);
        _name.MaxWidth = 90 * Scale;
        _value = TileText("Top CPU", 10, null, "Dock.Dim");
        _ = Refresh();
        return Frame(Ui.Row(7 * Scale, TileGlyph("", 15), Ui.Col(0, _name, _value)), null, 10);
    }

    public override void Tick() { if (++_tick % 3 == 0) _ = Refresh(); }

    async Task Refresh()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var top = await SystemSampler.TopProcessesAsync(8);
            _last = top;
            if (top.Count > 0 && _name != null && _value != null) { _name.Text = top[0].Name; _value.Text = $"{top[0].Cpu:0.0}% CPU"; }
            if (CardOpen) FillList();
        }
        finally { _busy = false; }
    }

    void FillList()
    {
        if (_list == null) return;
        _list.Children.Clear();
        foreach (var (name, cpu, _) in _last) _list.Children.Add(Ui.Line(Ui.Text(name, 12.5), Ui.Text($"{cpu:0.0}%", 12.5, FontWeights.SemiBold)));
        if (_last.Count == 0) _list.Children.Add(Ui.Dim("Measuring…", 12.5));
    }

    public override FrameworkElement? BuildCard()
    {
        _list = new StackPanel();
        FillList();
        _ = Refresh();
        return Ui.Col(10, Ui.Title("Busiest apps"), _list, Ui.Button("Task Manager", () => { Apps.Open("taskmgr.exe"); Host.CloseCard(); }));
    }
}

/// <summary>The app using the most memory.</summary>
public sealed class TopRamWidget : Widget
{
    public override string Id => "topram";
    public override string Name => "Top memory";
    public override string Category => "System";
    public override string Description => "Which app is using the most memory.";
    TextBlock? _name, _value;
    StackPanel? _list;
    List<(string Name, long Mem)> _last = [];
    int _tick;
    bool _busy;

    protected override FrameworkElement BuildTile()
    {
        _name = TileText("…", 11.5, FontWeights.SemiBold);
        _name.MaxWidth = 90 * Scale;
        _value = TileText("Top memory", 10, null, "Dock.Dim");
        _ = Refresh();
        return Frame(Ui.Row(7 * Scale, TileGlyph("", 15), Ui.Col(0, _name, _value)), null, 10);
    }

    public override void Tick() { if (++_tick % 5 == 0) _ = Refresh(); }

    async Task Refresh()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var top = await SystemSampler.TopMemoryAsync(8);
            _last = top;
            if (top.Count > 0 && _name != null && _value != null) { _name.Text = top[0].Name; _value.Text = Fmt.Bytes(top[0].Mem); }
            if (CardOpen) FillList();
        }
        finally { _busy = false; }
    }

    void FillList()
    {
        if (_list == null) return;
        _list.Children.Clear();
        foreach (var (name, mem) in _last) _list.Children.Add(Ui.Line(Ui.Text(name, 12.5), Ui.Text(Fmt.Bytes(mem), 12.5, FontWeights.SemiBold)));
        if (_last.Count == 0) _list.Children.Add(Ui.Dim("Measuring…", 12.5));
    }

    public override FrameworkElement? BuildCard()
    {
        _list = new StackPanel();
        FillList();
        _ = Refresh();
        return Ui.Col(10, Ui.Title("Using the most memory"), _list, Ui.Button("Task Manager", () => { Apps.Open("taskmgr.exe"); Host.CloseCard(); }));
    }
}

/// <summary>Graphics card load, and temperature and memory on NVIDIA cards.</summary>
public sealed class GpuWidget : Widget
{
    public override string Id => "gpu";
    public override string Name => "GPU";
    public override string Category => "System";
    public override string Description => "Graphics card load, plus temperature and video memory on NVIDIA cards.";
    public override double CardWidth => 320;
    Sparkline? _spark, _big;
    TextBlock? _load, _temp, _bigLoad, _detail;
    bool _started;

    public GpuWidget() { Gpu.Instance.Changed += Update; }
    public override void Detach() { Gpu.Instance.Changed -= Update; if (_started) Gpu.Instance.Stop(); }

    protected override FrameworkElement BuildTile()
    {
        if (!_started) { Gpu.Instance.Start(Application.Current.Dispatcher); _started = true; }
        _spark = new Sparkline { Data = Gpu.Instance.Load, Width = 34 * Scale, Height = Size * 0.42, BrushKey = "Good" };
        _load = TileText("…", 13.5, FontWeights.SemiBold);
        _temp = TileText("GPU", 10, null, "Dock.Dim");
        var col = Ui.Col(0, _temp, _load);
        col.Width = 44 * Scale;
        Update();
        return Frame(Ui.Row(8 * Scale, _spark, col), null, 10);
    }

    void Update()
    {
        var r = Gpu.Instance.Latest;
        if (r == null || _load == null || _temp == null) return;
        _load.Text = $"{r.Load:0}%";
        _temp.Text = r.Temp is { } t ? $"GPU {t:0}°" : "GPU";
        _spark?.Refresh();
        if (CardOpen && _bigLoad != null && _detail != null)
        {
            _bigLoad.Text = $"{r.Load:0}%";
            _detail.Text = string.Join(" · ", new[]
            {
                r.Temp is { } tt ? $"{tt:0} °C" : null,
                r.MemUsedMb is { } u && r.MemTotalMb is { } tot ? $"{u / 1024:0.0} of {tot / 1024:0.0} GB video memory" : null,
            }.Where(x => x != null));
            _big?.Refresh();
        }
    }

    public override FrameworkElement? BuildCard()
    {
        var r = Gpu.Instance.Latest;
        _bigLoad = Ui.Text("", 26, FontWeights.SemiBold);
        _detail = Ui.Dim("", 12);
        _big = new Sparkline { Data = Gpu.Instance.Load, Height = 70, BrushKey = "Good" };
        Update();
        var note = r?.Temp == null ? Ui.Wrap(Ui.Dim("Temperature and video memory are shown for NVIDIA cards.", 11.5)) : (UIElement)new Border();
        return Ui.Col(10, Ui.Line(Ui.Col(0, Ui.Title("Graphics"), Ui.Dim(r?.Name ?? "Reading…", 11.5)), _bigLoad), _big, _detail, note);
    }
}

/// <summary>Screen brightness, including external monitors that support it.</summary>
public sealed class BrightnessWidget : Widget
{
    public override string Id => "brightness";
    public override string Name => "Brightness";
    public override string Category => "System";
    public override string Description => "Screen brightness, for laptop screens and most external monitors. Scroll on it to adjust.";
    public override bool IsIcon => true;
    int _level = -2; // -2 not read yet, -1 not supported
    TextBlock? _glyph;
    Slider? _slider;
    TextBlock? _pct;
    CancellationTokenSource? _pending;

    protected override FrameworkElement BuildTile()
    {
        _glyph = TileGlyph("", 18);
        _ = Read();
        return IconFrame(_glyph);
    }

    async Task Read()
    {
        _level = await Task.Run(Brightness.Get);
        if (CardOpen) Host.RefreshCard(this);
    }

    /// <summary>Monitors are slow to talk to, so changes are sent after a short pause.</summary>
    void SetSoon(int pct)
    {
        _level = Math.Clamp(pct, 0, 100);
        if (_pct != null) _pct.Text = $"{_level}%";
        _pending?.Cancel();
        var cts = _pending = new CancellationTokenSource();
        Task.Delay(150, cts.Token).ContinueWith(t => { if (!t.IsCanceled) Brightness.Set(_level); }, TaskScheduler.Default);
    }

    public override void OnWheel(int delta)
    {
        if (_level < 0) return;
        SetSoon(_level + (delta > 0 ? 5 : -5));
        if (_slider != null) _slider.Value = _level;
    }

    public override FrameworkElement? BuildCard()
    {
        if (_level == -2) return Ui.Col(10, Ui.Title("Brightness"), Ui.Dim("Asking your screens…"));
        if (_level < 0)
            return Ui.Col(10, Ui.Title("Brightness"),
                Ui.Wrap(Ui.Dim("None of your screens lets apps change its brightness. Some monitors need DDC/CI switched on in their own menu.", 12)),
                Ui.Button("Display settings", () => { QuickSettings.OpenSettings("display"); Host.CloseCard(); }));
        _pct = Ui.Text($"{_level}%", 13, FontWeights.SemiBold);
        _pct.Width = 40;
        _slider = Ui.Slider(0, 100, _level, v => SetSoon((int)v), 210);
        return Ui.Col(12, Ui.Title("Brightness"), Ui.Row(6, Ui.Glyph("", 15, "Card.Dim"), _slider, _pct),
            Ui.Wrap(Ui.Dim("Applies to every screen that supports it.", 11.5)));
    }
}

/// <summary>Switches Windows between light and dark mode in one click.</summary>
public sealed class ThemeToggleWidget : Widget
{
    public override string Id => "theme";
    public override string Name => "Light / dark";
    public override string Category => "System";
    public override string Description => "Switches Windows between light and dark mode in one click.";
    public override bool IsIcon => true;
    TextBlock? _glyph;

    protected override FrameworkElement BuildTile()
    {
        _glyph = TileGlyph("", 18);
        Tick();
        return IconFrame(_glyph);
    }

    public override void Tick() { if (_glyph != null) _glyph.Text = QuickSettings.IsDarkMode() ? "" : ""; }

    public override void OnClick()
    {
        QuickSettings.SetDarkMode(!QuickSettings.IsDarkMode());
        Tick();
    }
}

/// <summary>Opens the notification centre (Win+N).</summary>
public sealed class NotificationsWidget : Widget
{
    public override string Id => "notifications";
    public override string Name => "Notifications";
    public override string Category => "System";
    public override string Description => "Opens the Windows notification centre and calendar.";
    public override bool IsIcon => true;
    protected override FrameworkElement BuildTile() => IconFrame(TileGlyph("", 18));
    public override void OnClick() => Native.PressKeys(Native.VK_LWIN, 0x4E /* N */);
}

/// <summary>The keyboard language in use, like the taskbar's "ENG US".</summary>
public sealed class LanguageWidget : Widget
{
    public override string Id => "language";
    public override string Name => "Keyboard language";
    public override string Category => "System";
    public override string Description => "Shows the keyboard language you're typing in. Click to switch.";
    TextBlock? _lang, _region;

    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint threadId);

    protected override FrameworkElement BuildTile()
    {
        _lang = TileText("", 12, FontWeights.SemiBold);
        _region = TileText("", 10, null, "Dock.Dim");
        _lang.HorizontalAlignment = _region.HorizontalAlignment = HorizontalAlignment.Center;
        Tick();
        return Frame(Ui.Col(0, _lang, _region), Size, 4);
    }

    public override void Tick()
    {
        if (_lang == null || _region == null) return;
        // The layout of the window you're typing in, not Perch's own.
        var target = WindowTracker.Instance.LastActive;
        uint thread = target != IntPtr.Zero ? Native.GetWindowThreadProcessId(target, out _) : 0;
        int langId = (int)GetKeyboardLayout(thread).ToInt64() & 0xFFFF;
        try
        {
            var c = new CultureInfo(langId);
            _lang.Text = c.ThreeLetterISOLanguageName.ToUpperInvariant();
            _region.Text = c.IsNeutralCulture ? "" : new RegionInfo(c.Name).TwoLetterISORegionName;
        }
        catch { _lang.Text = "KB"; _region.Text = ""; }
    }

    public override void OnClick()
    {
        // Win+Space is Windows' own language switcher; switch back to the app first so it applies there.
        var target = WindowTracker.Instance.LastActive;
        if (target != IntPtr.Zero) WindowTracker.Activate(target);
        Native.PressKeys(Native.VK_LWIN, 0x20 /* Space */);
    }
}

/// <summary>Battery as a ring with the percentage inside.</summary>
public sealed class BatteryRingWidget : Widget
{
    public override string Id => "batteryring";
    public override string Name => "Battery ring";
    public override string Category => "System";
    public override string Description => "Battery charge as a ring, green while charging.";
    public override bool IsIcon => true;
    RingGauge? _ring;
    readonly BatteryWidget _full = new();

    protected override FrameworkElement BuildTile()
    {
        _ring = new RingGauge { Width = Size * 0.68, Height = Size * 0.68, Thickness = 3.5 * Scale, LabelSize = 10 * Scale };
        Tick();
        return IconFrame(_ring);
    }

    public override void Tick()
    {
        if (_ring == null) return;
        var s = SystemSampler.Instance.Latest;
        if (!s.HasBattery) { _ring.Value = 100; _ring.Label = "AC"; _ring.BrushKey = "Good"; return; }
        _ring.Value = s.BatteryPct;
        _ring.Label = $"{s.BatteryPct}";
        _ring.BrushKey = s.Charging || s.OnAc ? "Good" : s.BatteryPct <= 20 ? "Bad" : "Accent";
        _ring.InvalidateVisual();
    }

    public override FrameworkElement? BuildCard() { _full.Host = Host; return _full.BuildCard(); }
}
