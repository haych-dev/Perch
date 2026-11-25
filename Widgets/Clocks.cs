using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Perch.Core;

namespace Perch.Widgets;

/// <summary>A round clock face with hands. Click for the calendar.</summary>
public sealed class AnalogClockWidget : Widget
{
    public override string Id => "analog";
    public override string Name => "Analog clock";
    public override string Category => "Time";
    public override string Description => "A clock face with hands. Click for the calendar.";
    public override bool IsIcon => true;
    readonly ClockWidget _clock = new();
    Face? _face;

    protected override FrameworkElement BuildTile()
    {
        _face = new Face { Width = Size * 0.78, Height = Size * 0.78 };
        return IconFrame(_face);
    }

    public override void Tick()
    {
        _face?.InvalidateVisual();
        if (CardOpen) _clock.Tick();
    }

    public override FrameworkElement? BuildCard() { _clock.Host = Host; _clock.CardOpen = true; return _clock.BuildCard(); }
    public override void OnCardClosed() => _clock.CardOpen = false;

    sealed class Face : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            double s = Math.Min(ActualWidth, ActualHeight);
            if (s <= 0) return;
            var c = new Point(ActualWidth / 2, ActualHeight / 2);
            double r = s / 2;
            var text = Theme.Brush("Dock.Text");
            dc.DrawEllipse(Theme.Brush("Tile.Hover"), new Pen(Theme.Brush("Dock.Dim"), Math.Max(1, s / 40)), c, r - 0.5, r - 0.5);
            for (int i = 0; i < 12; i++)
            {
                double a = i * Math.PI / 6, inner = i % 3 == 0 ? r * 0.72 : r * 0.8;
                var pen = new Pen(i % 3 == 0 ? text : Theme.Brush("Dock.Dim"), Math.Max(1, s / (i % 3 == 0 ? 22 : 34))) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(pen, new Point(c.X + Math.Sin(a) * inner, c.Y - Math.Cos(a) * inner), new Point(c.X + Math.Sin(a) * r * 0.88, c.Y - Math.Cos(a) * r * 0.88));
            }
            var now = DateTime.Now;
            void Hand(double turns, double len, double width, Brush b)
            {
                double a = turns * 2 * Math.PI;
                var pen = new Pen(b, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(pen, new Point(c.X - Math.Sin(a) * r * 0.12, c.Y + Math.Cos(a) * r * 0.12), new Point(c.X + Math.Sin(a) * len, c.Y - Math.Cos(a) * len));
            }
            Hand((now.Hour % 12 + now.Minute / 60.0) / 12, r * 0.5, Math.Max(2, s / 14), text);
            Hand((now.Minute + now.Second / 60.0) / 60, r * 0.74, Math.Max(1.6, s / 20), text);
            Hand(now.Second / 60.0, r * 0.8, Math.Max(1, s / 40), Theme.Brush("Accent"));
            dc.DrawEllipse(Theme.Brush("Accent"), null, c, s / 22, s / 22);
        }
    }
}

/// <summary>Hours and minutes on two split-flap cards.</summary>
public sealed class FlipClockWidget : Widget
{
    public override string Id => "flipclock";
    public override string Name => "Flip clock";
    public override string Category => "Time";
    public override string Description => "Hours and minutes on two flip cards.";
    readonly ClockWidget _clock = new();
    TextBlock? _h, _m, _ampm;

    protected override FrameworkElement BuildTile()
    {
        Border Card(out TextBlock t)
        {
            t = TileText("", 19, FontWeights.Bold);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            var g = new Grid { Width = 34 * Scale, Height = Size * 0.74 };
            g.Children.Add(t);
            // The fold across the middle of each card.
            var fold = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.55, Background = new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)) };
            g.Children.Add(fold);
            var b = new Border { CornerRadius = new CornerRadius(6 * Scale), Child = g, ClipToBounds = true };
            b.Background = new LinearGradientBrush(Color.FromArgb(0x60, 0x30, 0x33, 0x3C), Color.FromArgb(0x90, 0x10, 0x11, 0x16), 90);
            return b;
        }
        var row = Ui.Row(4 * Scale, Card(out _h), Card(out _m));
        _ampm = TileText("", 9, FontWeights.SemiBold, "Dock.Dim");
        _ampm.VerticalAlignment = VerticalAlignment.Bottom;
        _ampm.Margin = new Thickness(3 * Scale, 0, 0, 6 * Scale);
        row.Children.Add(_ampm);
        Tick();
        return Frame(row, null, 6);
    }

    public override void Tick()
    {
        var now = DateTime.Now;
        bool h24 = Settings.Current.Clock24h;
        int hour = h24 ? now.Hour : (now.Hour % 12 == 0 ? 12 : now.Hour % 12);
        if (_h != null) _h.Text = h24 ? hour.ToString("00") : hour.ToString();
        if (_m != null) _m.Text = now.Minute.ToString("00");
        if (_ampm != null) _ampm.Text = h24 ? "" : (now.Hour < 12 ? "AM" : "PM");
        if (CardOpen) _clock.Tick();
    }

    public override FrameworkElement? BuildCard() { _clock.Host = Host; _clock.CardOpen = true; return _clock.BuildCard(); }
    public override void OnCardClosed() => _clock.CardOpen = false;
}

/// <summary>A seven-segment display, like a bedside clock.</summary>
public sealed class SegmentClockWidget : Widget
{
    public override string Id => "segment";
    public override string Name => "Segment clock";
    public override string Category => "Time";
    public override string Description => "The time on a glowing seven-segment display.";
    readonly ClockWidget _clock = new();
    Segments? _seg;

    protected override FrameworkElement BuildTile()
    {
        // Four digits (0.52 h wide), three gaps (0.18 h) and the colon (0.22 h): 2.84 times the height.
        _seg = new Segments { Height = Size * 0.46, Width = Size * 0.46 * 2.84 };
        Tick();
        return Frame(_seg, null, 10);
    }

    public override void Tick()
    {
        if (_seg != null)
        {
            var now = DateTime.Now;
            int hour = Settings.Current.Clock24h ? now.Hour : (now.Hour % 12 == 0 ? 12 : now.Hour % 12);
            _seg.Text = $"{hour,2}{now.Minute:00}";
            _seg.Colon = now.Second % 2 == 0;
            _seg.InvalidateVisual();
        }
        if (CardOpen) _clock.Tick();
    }

    public override FrameworkElement? BuildCard() { _clock.Host = Host; _clock.CardOpen = true; return _clock.BuildCard(); }
    public override void OnCardClosed() => _clock.CardOpen = false;

    sealed class Segments : FrameworkElement
    {
        public string Text = "1200";
        public bool Colon = true;
        // Segments a–g for each digit.
        static readonly string[] Lit = ["abcdef", "bc", "abdeg", "abcdg", "bcfg", "acdfg", "acdefg", "abc", "abcdefg", "abcdfg"];

        protected override void OnRender(DrawingContext dc)
        {
            double h = ActualHeight, w = h * 0.52, t = h * 0.12, gap = h * 0.18, colonW = h * 0.22;
            var on = Theme.Brush("Accent");
            var offC = Theme.ColorOf("Dock.Text");
            var off = new SolidColorBrush(Color.FromArgb(0x1C, offC.R, offC.G, offC.B));
            double x = 0;
            for (int i = 0; i < 4; i++)
            {
                if (i == 2)
                {
                    var cb = Colon ? on : off;
                    dc.DrawEllipse(cb, null, new Point(x + colonW / 2 - gap / 4, h * 0.32), t * 0.5, t * 0.5);
                    dc.DrawEllipse(cb, null, new Point(x + colonW / 2 - gap / 4, h * 0.68), t * 0.5, t * 0.5);
                    x += colonW;
                }
                char ch = Text[i];
                string lit = ch == ' ' ? "" : Lit[ch - '0'];
                Digit(dc, x, w, h, t, lit, on, off);
                x += w + gap;
            }
        }

        static void Digit(DrawingContext dc, double x, double w, double h, double t, string lit, Brush on, Brush off)
        {
            double m = h / 2;
            void Seg(char id, double x1, double y1, double x2, double y2)
            {
                var b = lit.Contains(id) ? on : off;
                bool horizontal = Math.Abs(y1 - y2) < 0.01;
                var r = horizontal ? new Rect(x1 + t * 0.6, y1 - t / 2, Math.Max(0, x2 - x1 - t * 1.2), t) : new Rect(x1 - t / 2, y1 + t * 0.6, t, Math.Max(0, y2 - y1 - t * 1.2));
                dc.DrawRoundedRectangle(b, null, r, t / 2, t / 2);
            }
            double l = x + t / 2, rr = x + w - t / 2, top = t / 2, bot = h - t / 2;
            Seg('a', l, top, rr, top);
            Seg('b', rr, top, rr, m);
            Seg('c', rr, m, rr, bot);
            Seg('d', l, bot, rr, bot);
            Seg('e', l, m, l, bot);
            Seg('f', l, top, l, m);
            Seg('g', l, m, rr, m);
        }
    }
}

/// <summary>The time in up to two other places, from the clock card's world clocks.</summary>
public sealed class WorldClocksWidget : Widget
{
    public override string Id => "worldclocks";
    public override string Name => "World clocks";
    public override string Category => "Time";
    public override string Description => "The time in two other cities. Add cities from the clock's card.";
    readonly ClockWidget _clock = new();
    StackPanel? _row;

    protected override FrameworkElement BuildTile()
    {
        _row = new StackPanel { Orientation = Orientation.Horizontal };
        Tick();
        return Frame(_row, null, 10);
    }

    public override void Tick()
    {
        if (_row != null && (DateTime.Now.Second == 0 || _row.Children.Count == 0)) Fill();
        if (CardOpen) _clock.Tick();
    }

    void Fill()
    {
        if (_row == null) return;
        _row.Children.Clear();
        var zones = Settings.Current.WorldClocks.Take(2).ToList();
        if (zones.Count == 0)
        {
            _row.Children.Add(Ui.Col(0, TileText("World clocks", 12.5, FontWeights.SemiBold), TileText("Add a city", 10, null, "Dock.Dim")));
            return;
        }
        foreach (var id in zones)
        {
            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(id); } catch { continue; }
            var t = TimeZoneInfo.ConvertTime(DateTime.Now, tz);
            var col = Ui.Col(0, TileText(ClockWidget.FormatTime(t, false), 13, FontWeights.SemiBold), TileText(City(tz), 10, null, "Dock.Dim"));
            col.Margin = new Thickness(_row.Children.Count > 0 ? 12 * Scale : 0, 0, 0, 0);
            col.MaxWidth = 90 * Scale;
            _row.Children.Add(col);
        }
    }

    /// <summary>"(UTC-08:00) Pacific Time (US &amp; Canada)" → "Pacific Time"; "(UTC+00:00) Dublin, Edinburgh…" → "Dublin".</summary>
    static string City(TimeZoneInfo tz)
    {
        var n = tz.DisplayName;
        int p = n.IndexOf(')');
        var s = (p > 0 ? n[(p + 1)..] : n).Split(',')[0];
        return System.Text.RegularExpressions.Regex.Replace(s, @"\s*\(.*?\)", "").Trim();
    }

    public override FrameworkElement? BuildCard() { _clock.Host = Host; _clock.CardOpen = true; return _clock.BuildCard(); }
    public override void OnCardClosed() { _clock.CardOpen = false; Fill(); }
}

/// <summary>Which day of the year it is.</summary>
public sealed class DayNumberWidget : Widget
{
    public override string Id => "daynumber";
    public override string Name => "Day number";
    public override string Category => "Time";
    public override string Description => "Which day of the year it is, and the week number.";
    TextBlock? _day, _week;

    protected override FrameworkElement BuildTile()
    {
        _day = TileText("", 15, FontWeights.SemiBold);
        _week = TileText("", 10, null, "Dock.Dim");
        Tick();
        return Frame(Ui.Col(0, _day, _week), null, 11);
    }

    public override void Tick()
    {
        var now = DateTime.Now;
        int total = DateTime.IsLeapYear(now.Year) ? 366 : 365;
        if (_day != null) _day.Text = $"Day {now.DayOfYear}";
        if (_week != null) _week.Text = $"Week {ISOWeek.GetWeekOfYear(now)} · {total - now.DayOfYear} left";
    }

    public override void OnClick() { }
}
