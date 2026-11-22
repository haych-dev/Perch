using System.Globalization;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Perch.Core;
using Perch.Dock;

namespace Perch.Widgets;

public sealed class ClockWidget : Widget
{
    public override string Id => "clock";
    public override string Name => "Clock";
    public override string Category => "Time";
    public override string Description => "Time and date. Click for a calendar and world clocks.";
    public override double CardWidth => 320;

    TextBlock? _time, _date;
    TextBlock? _cardTime, _cardDate;
    StackPanel? _zones;
    DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    internal static string FormatTime(DateTime t, bool seconds)
    {
        var s = Settings.Current;
        string f = s.Clock24h ? (seconds ? "HH:mm:ss" : "HH:mm") : (seconds ? "h:mm:ss" : "h:mm");
        string text = t.ToString(f, CultureInfo.CurrentCulture);
        if (!s.Clock24h) text += t.Hour < 12 ? " AM" : " PM";
        return text;
    }

    protected override FrameworkElement BuildTile()
    {
        _time = TileText("", 15, FontWeights.SemiBold);
        _date = TileText("", 11, null, "Dock.Dim");
        _time.HorizontalAlignment = _date.HorizontalAlignment = HorizontalAlignment.Center;
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _time } };
        if (Settings.Current.ClockDate) col.Children.Add(_date);
        else _time.FontSize = 17 * Scale;
        Tick();
        return Frame(col, null, 12);
    }

    public override void Tick()
    {
        var now = DateTime.Now;
        if (_time != null) _time.Text = FormatTime(now, Settings.Current.ClockSeconds);
        if (_date != null) _date.Text = now.ToString("ddd d MMM", CultureInfo.CurrentCulture);
        if (CardOpen)
        {
            if (_cardTime != null) _cardTime.Text = FormatTime(now, true);
            if (_cardDate != null) _cardDate.Text = now.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture);
            if (_zones != null && now.Second == 0) FillZones();
        }
    }

    public override FrameworkElement? BuildCard()
    {
        _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        _cardTime = Ui.Text("", 30, FontWeights.SemiBold);
        _cardTime.FontFamily = (FontFamily)Application.Current.Resources["F.Display"];
        _cardDate = Ui.Dim("", 13);
        var cal = new ContentControl { Content = Calendar(_month) };
        var monthTitle = Ui.Title(_month.ToString("MMMM yyyy", CultureInfo.CurrentCulture));
        void Go(int delta)
        {
            _month = _month.AddMonths(delta);
            cal.Content = Calendar(_month);
            monthTitle.Text = _month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        }
        Button prev = Ui.IconButton("", () => Go(-1), 12);
        Button next = Ui.IconButton("", () => Go(1), 12);
        _zones = new StackPanel();
        FillZones();
        Tick();
        return Ui.Col(12,
            Ui.Col(0, _cardTime, _cardDate),
            Ui.Divider(),
            Ui.Line(monthTitle, Ui.Row(0, prev, next)),
            cal,
            Ui.Divider(),
            _zones);
    }

    FrameworkElement Calendar(DateTime month)
    {
        var culture = CultureInfo.CurrentCulture;
        var grid = new UniformGrid7();
        var first = culture.DateTimeFormat.FirstDayOfWeek;
        for (int i = 0; i < 7; i++)
        {
            var dn = Ui.Dim(culture.DateTimeFormat.GetShortestDayName((DayOfWeek)(((int)first + i) % 7)), 11);
            dn.HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children.Add(dn);
        }
        int offset = ((int)month.DayOfWeek - (int)first + 7) % 7;
        var start = month.AddDays(-offset);
        for (int i = 0; i < 42; i++)
        {
            var d = start.AddDays(i);
            bool today = d == DateTime.Today, inMonth = d.Month == month.Month;
            var t = Ui.Text(d.Day.ToString(), 12.5, today ? FontWeights.SemiBold : FontWeights.Normal, today ? "OnAccent" : inMonth ? "Card.Text" : "Card.Dim");
            t.HorizontalAlignment = HorizontalAlignment.Center;
            if (!inMonth) t.Opacity = 0.5;
            var cell = new Border { Height = 30, Width = 30, CornerRadius = new CornerRadius(15), Child = t, HorizontalAlignment = HorizontalAlignment.Center };
            if (today) cell.SetResourceReference(Border.BackgroundProperty, "Accent");
            grid.Children.Add(cell);
        }
        return grid;
    }

    void FillZones()
    {
        if (_zones == null) return;
        _zones.Children.Clear();
        var s = Settings.Current;
        foreach (var id in s.WorldClocks.ToList())
        {
            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(id); } catch { continue; }
            var t = TimeZoneInfo.ConvertTime(DateTime.Now, tz);
            double diff = (tz.GetUtcOffset(DateTime.UtcNow) - TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow)).TotalHours;
            string rel = diff == 0 ? "Same time" : $"{(diff > 0 ? "+" : "")}{diff:0.#} h" + (t.Date != DateTime.Today ? (t.Date > DateTime.Today ? ", tomorrow" : ", yesterday") : "");
            var name = Ui.Text(CityOf(tz), 13);
            var right = Ui.Row(8, Ui.Text(FormatTime(t, false), 13, FontWeights.SemiBold), Ui.IconButton("", () => { Settings.Current.WorldClocks.Remove(id); Settings.SaveSoon(); FillZones(); }, 9));
            _zones.Children.Add(Ui.Line(Ui.Col(0, name, Ui.Dim(rel, 11)), right));
        }
        _zones.Children.Add(Ui.Button("Add a world clock", () => AddZone(), "Flat"));
    }

    void AddZone()
    {
        if (_zones == null) return;
        _zones.Children.Clear();
        var field = Ui.Field(out var box, "", "City or time zone");
        var results = new StackPanel();
        void Search()
        {
            results.Children.Clear();
            var q = box.Text.Trim();
            if (q.Length < 2) return;
            foreach (var tz in TimeZoneInfo.GetSystemTimeZones().Where(z => z.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) || z.Id.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(6))
            {
                var id = tz.Id;
                var label = Ui.Text(tz.DisplayName, 12);
                label.MaxWidth = 270;
                results.Children.Add(Ui.ListRow(label, () =>
                {
                    if (!Settings.Current.WorldClocks.Contains(id)) Settings.Current.WorldClocks.Add(id);
                    Settings.SaveSoon();
                    FillZones();
                }));
            }
        }
        box.TextChanged += (_, _) => Search();
        _zones.Children.Add(field);
        _zones.Children.Add(results);
        _zones.Children.Add(Ui.Button("Cancel", FillZones, "Flat"));
        box.Loaded += (_, _) => box.Focus();
    }

    static string CityOf(TimeZoneInfo tz)
    {
        var n = tz.DisplayName;
        int p = n.IndexOf(')');
        var cities = p > 0 ? n[(p + 1)..].Trim() : n;
        return cities.Split(',')[0].Trim();
    }
}

/// <summary>A seven-column grid for the calendar.</summary>
public sealed class UniformGrid7 : System.Windows.Controls.Primitives.UniformGrid
{
    public UniformGrid7() { Columns = 7; }
}

public sealed class DateWidget : Widget
{
    public override string Id => "date";
    public override string Name => "Date";
    public override string Category => "Time";
    public override string Description => "Today's date as a calendar page.";
    public override bool IsIcon => true;
    TextBlock? _month, _day;
    readonly ClockWidget _clock = new();

    protected override FrameworkElement BuildTile()
    {
        _month = Ui.Text("", 9 * Scale, FontWeights.Bold, "OnAccent");
        _month.HorizontalAlignment = HorizontalAlignment.Center;
        var band = new Border { Height = Size * 0.3, Child = _month, CornerRadius = new CornerRadius(Size * 0.22, Size * 0.22, 0, 0) };
        band.SetResourceReference(Border.BackgroundProperty, "Bad");
        _day = Ui.Text("", 19 * Scale, FontWeights.SemiBold, "Card.Text");
        _day.HorizontalAlignment = HorizontalAlignment.Center;
        var page = new Border { Width = Size * 0.9, Height = Size * 0.9, CornerRadius = new CornerRadius(Size * 0.22), ClipToBounds = true, Background = Brushes.White };
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition());
        g.Children.Add(band);
        Grid.SetRow(_day, 1);
        _day.Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x22));
        g.Children.Add(_day);
        page.Child = g;
        Tick();
        return new Grid { Width = Size, Height = Size, Children = { page } };
    }

    public override void Tick()
    {
        if (_month != null) _month.Text = DateTime.Now.ToString("MMM").ToUpperInvariant();
        if (_day != null) _day.Text = DateTime.Now.Day.ToString();
        if (CardOpen) _clock.Tick();
    }

    public override FrameworkElement? BuildCard() { _clock.Host = Host; _clock.CardOpen = true; return _clock.BuildCard(); }
    public override void OnCardClosed() => _clock.CardOpen = false;
}

public sealed class DayProgressWidget : Widget
{
    public override string Id => "progress";
    public override string Name => "Day progress";
    public override string Category => "Time";
    public override string Description => "How much of the day, week, month and year has gone.";
    public override bool IsIcon => true;
    RingGauge? _ring;

    static double DayPct => DateTime.Now.TimeOfDay.TotalHours / 24 * 100;

    protected override FrameworkElement BuildTile()
    {
        _ring = new RingGauge { Width = Size * 0.72, Height = Size * 0.72, Thickness = 3.5 * Scale, LabelSize = 10.5 * Scale };
        Tick();
        return IconFrame(_ring);
    }

    public override void Tick()
    {
        if (_ring == null) return;
        _ring.Label = $"{DayPct:0}%";
        _ring.Value = DayPct;
    }

    public override FrameworkElement? BuildCard()
    {
        var now = DateTime.Now;
        int dow = ((int)now.DayOfWeek - (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek + 7) % 7;
        double week = (dow + now.TimeOfDay.TotalDays) / 7 * 100;
        double month = (now.Day - 1 + now.TimeOfDay.TotalDays) / DateTime.DaysInMonth(now.Year, now.Month) * 100;
        int daysInYear = DateTime.IsLeapYear(now.Year) ? 366 : 365;
        double year = (now.DayOfYear - 1 + now.TimeOfDay.TotalDays) / daysInYear * 100;
        UIElement Row(string label, double pct, string detail) => Ui.Col(5, Ui.Line(Ui.Text(label, 13), Ui.Text($"{pct:0}%", 13, FontWeights.SemiBold)), Ui.Bar(pct), Ui.Dim(detail, 11));
        return Ui.Col(14,
            Ui.Title("Progress"),
            Row("Today", DayPct, $"{24 - now.TimeOfDay.TotalHours:0.0} hours left"),
            Row("This week", week, $"{7 - dow - 1} days after today"),
            Row(now.ToString("MMMM"), month, $"{DateTime.DaysInMonth(now.Year, now.Month) - now.Day} days left"),
            Row(now.Year.ToString(), year, $"Day {now.DayOfYear} of {daysInYear}"));
    }
}

/// <summary>Focus sessions (pomodoro), a countdown timer and a stopwatch. State survives dock rebuilds.</summary>
public sealed class FocusWidget : Widget
{
    public override string Id => "focus";
    public override string Name => "Focus timer";
    public override string Category => "Productivity";
    public override string Description => "Focus sessions with breaks, a countdown timer and a stopwatch.";
    public override double CardWidth => 300;

    enum Mode { Focus, Break, Timer, Stopwatch }
    static Mode _mode = Mode.Focus;
    static bool _running;
    static DateTime _end;              // countdown end (when running)
    static TimeSpan _left = TimeSpan.FromMinutes(25), _total = TimeSpan.FromMinutes(25);
    static DateTime _swStart;
    static TimeSpan _swAcc;
    static int _sessions;
    static readonly List<TimeSpan> Laps = [];

    RingGauge? _ring;
    TextBlock? _tileText;
    TextBlock? _big, _sub;
    Button? _startBtn;
    StackPanel? _laps;

    static TimeSpan Remaining => _running && _mode != Mode.Stopwatch ? _end - DateTime.Now : _left;
    static TimeSpan Elapsed => _running ? _swAcc + (DateTime.Now - _swStart) : _swAcc;

    protected override FrameworkElement BuildTile()
    {
        _ring = new RingGauge { Width = Size * 0.56, Height = Size * 0.56, Thickness = 3 * Scale, BrushKey = "Bad" };
        _tileText = TileText("Focus", 13, FontWeights.SemiBold);
        Tick();
        return Frame(Ui.Row(8 * Scale, _ring, _tileText), null, 10);
    }

    public override void Tick()
    {
        if (_running && _mode != Mode.Stopwatch && Remaining <= TimeSpan.Zero) Finish();
        if (_tileText != null && _ring != null)
        {
            if (_mode == Mode.Stopwatch) { _tileText.Text = Format(Elapsed); _ring.Value = Elapsed.TotalSeconds % 60 / 60 * 100; }
            else if (_running || _left != _total) { _tileText.Text = Format(Remaining); _ring.Value = 100 - Remaining.TotalSeconds / Math.Max(1, _total.TotalSeconds) * 100; }
            else { _tileText.Text = _mode == Mode.Timer ? "Timer" : "Focus"; _ring.Value = 0; }
            _ring.BrushKey = _mode == Mode.Break ? "Good" : _mode == Mode.Focus ? "Bad" : "Accent";
            _ring.InvalidateVisual();
        }
        if (CardOpen) UpdateCard();
    }

    static string Format(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
    }

    void Finish()
    {
        _running = false;
        PlayChime();
        if (_mode == Mode.Focus) { _sessions++; SetMode(Mode.Break); Start(); }
        else if (_mode == Mode.Break) SetMode(Mode.Focus);
        else { _left = TimeSpan.Zero; }
        Host.OpenCard(this);
    }

    static void PlayChime()
    {
        try
        {
            var wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");
            if (File.Exists(wav)) new SoundPlayer(wav).Play();
            else SystemSounds.Exclamation.Play();
        }
        catch { }
    }

    static void SetMode(Mode m)
    {
        _mode = m;
        _running = false;
        var s = Settings.Current;
        _total = m switch { Mode.Focus => TimeSpan.FromMinutes(s.FocusMinutes), Mode.Break => TimeSpan.FromMinutes(_sessions % 4 == 0 && _sessions > 0 ? s.BreakMinutes * 3 : s.BreakMinutes), _ => _total };
        if (m != Mode.Timer && m != Mode.Stopwatch) _left = _total;
    }

    static void Start()
    {
        if (_mode == Mode.Stopwatch) { _swStart = DateTime.Now; _running = true; return; }
        if (_left <= TimeSpan.Zero) _left = _total;
        _end = DateTime.Now + _left;
        _running = true;
    }

    static void Pause()
    {
        if (_mode == Mode.Stopwatch) _swAcc = Elapsed;
        else _left = Remaining;
        _running = false;
    }

    static void Reset()
    {
        _running = false;
        if (_mode == Mode.Stopwatch) { _swAcc = TimeSpan.Zero; Laps.Clear(); }
        else _left = _total;
    }

    public override FrameworkElement? BuildCard()
    {
        _big = Ui.Text("", 40, FontWeights.SemiBold);
        _big.FontFamily = (FontFamily)Application.Current.Resources["F.Display"];
        _big.HorizontalAlignment = HorizontalAlignment.Center;
        _sub = Ui.Dim("", 12.5);
        _sub.HorizontalAlignment = HorizontalAlignment.Center;
        _startBtn = Ui.Button("Start", () => { if (_running) Pause(); else Start(); Tick(); }, "Primary");
        _startBtn.MinWidth = 96;
        var reset = Ui.Button("Reset", () => { Reset(); Tick(); });
        var buttons = Ui.Row(8, _startBtn, reset);
        buttons.HorizontalAlignment = HorizontalAlignment.Center;

        var modes = Ui.Segmented(new[] { (Mode.Focus, "Focus"), (Mode.Timer, "Timer"), (Mode.Stopwatch, "Stopwatch") }, _mode == Mode.Break ? Mode.Focus : _mode,
            m => { if (m != _mode && !(m == Mode.Focus && _mode == Mode.Break)) { SetMode(m); Host.RefreshCard(this); } });
        modes.HorizontalAlignment = HorizontalAlignment.Center;

        UIElement extra;
        if (_mode == Mode.Timer)
        {
            var presets = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            foreach (int m in new[] { 1, 3, 5, 10, 15, 30, 60 })
            {
                var b = Ui.Button($"{m} min", () => { _total = _left = TimeSpan.FromMinutes(m); Start(); Tick(); });
                b.Margin = new Thickness(3);
                presets.Children.Add(b);
            }
            extra = presets;
        }
        else if (_mode == Mode.Stopwatch)
        {
            _laps = new StackPanel();
            var lap = Ui.Button("Lap", () => { if (_running) { Laps.Insert(0, Elapsed); FillLaps(); } });
            buttons.Children.Insert(1, lap);
            lap.Margin = new Thickness(8, 0, 0, 0);
            FillLaps();
            extra = Ui.Scroll(_laps, 120);
        }
        else
        {
            var s = Settings.Current;
            extra = Ui.Col(8,
                Ui.Line(Ui.Text("Focus length", 12.5), Ui.Segmented(new[] { (15, "15"), (25, "25"), (45, "45"), (60, "60") }, s.FocusMinutes,
                    v => { Settings.Current.FocusMinutes = v; Settings.SaveSoon(); if (_mode == Mode.Focus && !_running) { SetMode(Mode.Focus); Tick(); } })),
                Ui.Line(Ui.Text("Break", 12.5), Ui.Segmented(new[] { (5, "5"), (10, "10"), (15, "15") }, s.BreakMinutes,
                    v => { Settings.Current.BreakMinutes = v; Settings.SaveSoon(); })));
        }
        UpdateCard();
        return Ui.Col(14, modes, Ui.Col(0, _big, _sub), buttons, extra);
    }

    void FillLaps()
    {
        if (_laps == null) return;
        _laps.Children.Clear();
        for (int i = 0; i < Laps.Count; i++)
            _laps.Children.Add(Ui.Line(Ui.Dim($"Lap {Laps.Count - i}"), Ui.Text(Format(Laps[i]), 12.5)));
    }

    void UpdateCard()
    {
        if (_big == null || _sub == null || _startBtn == null) return;
        _big.Text = _mode == Mode.Stopwatch ? Format(Elapsed) : Format(Remaining);
        _sub.Text = _mode switch
        {
            Mode.Focus => _sessions > 0 ? $"Focus · {_sessions} done today" : "Focus",
            Mode.Break => "Break: step away for a bit",
            Mode.Timer => _left == TimeSpan.Zero && !_running ? "Done" : "Timer",
            _ => "Stopwatch",
        };
        ((TextBlock)_startBtn.Content).Text = _running ? "Pause" : (_mode == Mode.Stopwatch ? (_swAcc > TimeSpan.Zero ? "Resume" : "Start") : (_left != _total && _left > TimeSpan.Zero ? "Resume" : "Start"));
    }
}
