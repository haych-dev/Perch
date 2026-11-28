using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Perch.Core;

namespace Perch.Dock;

/// <summary>A filled area sparkline for a ring buffer.</summary>
public sealed class Sparkline : FrameworkElement
{
    public Ring? Data { get; set; }
    public double Max { get; set; } = 100;
    public bool AutoScale { get; set; }
    public string BrushKey { get; set; } = "Accent";

    public void Refresh() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var d = Data;
        double w = ActualWidth, h = ActualHeight;
        if (d == null || d.Count < 2 || w <= 0 || h <= 0) return;
        double max = AutoScale ? Math.Max(1, d.Max * 1.15) : Max;
        int n = d.Capacity;
        double step = w / (n - 1);
        int offset = n - d.Count;
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var lc = line.Open())
        using (var ac = area.Open())
        {
            for (int i = 0; i < d.Count; i++)
            {
                var p = new Point((offset + i) * step, h - Math.Clamp(d[i] / max, 0, 1) * (h - 1.5) - 0.75);
                if (i == 0) { lc.BeginFigure(p, false, false); ac.BeginFigure(new Point(p.X, h), true, true); ac.LineTo(p, false, false); }
                else { lc.LineTo(p, true, true); ac.LineTo(p, false, false); }
            }
            ac.LineTo(new Point(w, h), false, false);
        }
        line.Freeze();
        area.Freeze();
        var c = Theme.ColorOf(BrushKey);
        if (c != _color || _fill == null || _pen == null)
        {
            // Frozen and reused, so redrawing every second doesn't create (and track) new brushes each time.
            _color = c;
            _fill = new LinearGradientBrush(Color.FromArgb(0x70, c.R, c.G, c.B), Color.FromArgb(0x08, c.R, c.G, c.B), 90);
            _fill.Freeze();
            var stroke = new SolidColorBrush(c);
            stroke.Freeze();
            _pen = new Pen(stroke, 1.4) { LineJoin = PenLineJoin.Round };
            _pen.Freeze();
        }
        dc.DrawGeometry(_fill, null, area);
        dc.DrawGeometry(null, _pen, line);
    }

    Color _color;
    Brush? _fill;
    Pen? _pen;
}

/// <summary>A circular progress ring with optional centre text.</summary>
public sealed class RingGauge : FrameworkElement
{
    double _value;
    public double Value { get => _value; set { _value = value; InvalidateVisual(); } }
    public double Thickness { get; set; } = 3.5;
    public string BrushKey { get; set; } = "Accent";
    public string TrackKey { get; set; } = "Tile.Fill";
    public string? Label { get; set; }
    public double LabelSize { get; set; } = 10;
    public string LabelKey { get; set; } = "Dock.Text";

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double r = size / 2 - Thickness / 2;
        var track = new Pen(Theme.Brush(TrackKey), Thickness);
        dc.DrawEllipse(null, track, c, r, r);
        double v = Math.Clamp(Value, 0, 100);
        if (v > 0.5)
        {
            double a = v / 100 * 359.9;
            var start = new Point(c.X, c.Y - r);
            double rad = (a - 90) * Math.PI / 180;
            var end = new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(start, false, false);
                ctx.ArcTo(end, new Size(r, r), 0, a > 180, SweepDirection.Clockwise, true, false);
            }
            dc.DrawGeometry(null, new Pen(Theme.Brush(BrushKey), Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
        }
        if (Label != null)
        {
            var ft = new FormattedText(Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface((FontFamily)Application.Current.Resources["F.Ui"], FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                LabelSize, Theme.Brush(LabelKey), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
        }
    }
}

/// <summary>Simple vector weather icons, so nothing depends on colour emoji fonts.</summary>
public sealed class WeatherIcon : FrameworkElement
{
    Weather.Kind _kind;
    bool _day = true;
    public Weather.Kind Kind { get => _kind; set { _kind = value; InvalidateVisual(); } }
    public bool Day { get => _day; set { _day = value; InvalidateVisual(); } }

    static readonly Brush Sun = Frozen(Color.FromRgb(0xFF, 0xC5, 0x3D));
    static readonly Brush Moon = Frozen(Color.FromRgb(0xE6, 0xE1, 0xC8));
    static readonly Brush CloudLight = Frozen(Color.FromRgb(0xE9, 0xEE, 0xF5));
    static readonly Brush CloudDark = Frozen(Color.FromRgb(0x9B, 0xA6, 0xB8));
    static readonly Brush Rain = Frozen(Color.FromRgb(0x5A, 0xB4, 0xFF));
    static readonly Brush Bolt = Frozen(Color.FromRgb(0xFF, 0xD2, 0x4A));

    static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth, ActualHeight);
        if (s <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - s) / 2, (ActualHeight - s) / 2));
        dc.PushTransform(new ScaleTransform(s / 32, s / 32));
        switch (Kind)
        {
            case Weather.Kind.Sun:
                DrawSun(dc, new Point(16, 16), 6.5);
                break;
            case Weather.Kind.PartSun:
                DrawSun(dc, new Point(12, 12), 5.5);
                DrawCloud(dc, CloudLight, 4, 10);
                break;
            case Weather.Kind.Cloud:
                DrawCloud(dc, CloudDark, -3, 6);
                DrawCloud(dc, CloudLight, 2, 9);
                break;
            case Weather.Kind.Fog:
                DrawCloud(dc, CloudDark, 0, 4);
                var fog = new Pen(CloudLight, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(fog, new Point(7, 24), new Point(25, 24));
                dc.DrawLine(fog, new Point(10, 28), new Point(22, 28));
                break;
            case Weather.Kind.Rain:
                DrawCloud(dc, CloudLight, 0, 3);
                var rain = new Pen(Rain, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                for (int i = 0; i < 3; i++) dc.DrawLine(rain, new Point(11 + i * 5, 23), new Point(9 + i * 5, 28));
                break;
            case Weather.Kind.Snow:
                DrawCloud(dc, CloudLight, 0, 3);
                for (int i = 0; i < 3; i++) dc.DrawEllipse(CloudLight, null, new Point(10 + i * 6, 26 + (i % 2) * 2), 1.7, 1.7);
                break;
            case Weather.Kind.Storm:
                DrawCloud(dc, CloudDark, 0, 3);
                var bolt = Geometry.Parse("M17,19 L12,26 L16,26 L13,32 L21,23 L17,23 L19,19 Z");
                dc.DrawGeometry(Bolt, null, bolt);
                break;
        }
        dc.Pop();
        dc.Pop();
    }

    void DrawSun(DrawingContext dc, Point c, double r)
    {
        if (!Day)
        {
            var moon = new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(c, r + 1, r + 1), new EllipseGeometry(new Point(c.X + 4, c.Y - 3), r, r));
            dc.DrawGeometry(Moon, null, moon);
            return;
        }
        dc.DrawEllipse(Sun, null, c, r, r);
        var pen = new Pen(Sun, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            dc.DrawLine(pen, new Point(c.X + Math.Cos(a) * (r + 2.5), c.Y + Math.Sin(a) * (r + 2.5)), new Point(c.X + Math.Cos(a) * (r + 5), c.Y + Math.Sin(a) * (r + 5)));
        }
    }

    static void DrawCloud(DrawingContext dc, Brush b, double dx, double dy)
    {
        var g = new GeometryGroup { FillRule = FillRule.Nonzero };
        g.Children.Add(new EllipseGeometry(new Point(12 + dx, 13 + dy), 5.5, 5.5));
        g.Children.Add(new EllipseGeometry(new Point(18 + dx, 11 + dy), 7, 7));
        g.Children.Add(new RectangleGeometry(new Rect(7 + dx, 12 + dy, 18, 6.5), 3.2, 3.2));
        // Outline the merged shape so pale clouds still read on light cards.
        var outline = g.GetOutlinedPathGeometry();
        dc.DrawGeometry(b, CloudEdge, outline);
    }

    static readonly Pen CloudEdge = MakeEdge();
    static Pen MakeEdge()
    {
        var p = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0x6A, 0x76, 0x8A)), 0.9);
        p.Freeze();
        return p;
    }
}

/// <summary>A battery outline with a fill level.</summary>
public sealed class BatteryIcon : FrameworkElement
{
    int _pct;
    bool _charging;
    public int Percent { get => _pct; set { _pct = value; InvalidateVisual(); } }
    public bool Charging { get => _charging; set { _charging = value; InvalidateVisual(); } }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0) return;
        var text = Theme.Brush("Dock.Text");
        var body = new Rect(0.75, 0.75, w - 4, h - 1.5);
        dc.DrawRoundedRectangle(null, new Pen(text, 1.5), body, 3, 3);
        dc.DrawRoundedRectangle(text, null, new Rect(w - 2.6, h * 0.32, 2.4, h * 0.36), 1, 1);
        var fillKey = _charging ? "Good" : _pct <= 15 ? "Bad" : _pct <= 30 ? "Warn" : "Dock.Text";
        double fw = Math.Max(0, (body.Width - 4) * Math.Clamp(_pct, 0, 100) / 100);
        dc.DrawRoundedRectangle(Theme.Brush(fillKey), null, new Rect(body.X + 2, body.Y + 2, fw, body.Height - 4), 1.5, 1.5);
    }
}

/// <summary>The four-pane logo for the Start button.</summary>
public sealed class StartLogo : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth, ActualHeight);
        double gap = s * 0.08, q = (s - gap) / 2;
        double x0 = (ActualWidth - s) / 2, y0 = (ActualHeight - s) / 2;
        var brush = new LinearGradientBrush(Color.FromRgb(0x5C, 0xC8, 0xFF), Color.FromRgb(0x1A, 0x7C, 0xF0), 45);
        for (int i = 0; i < 4; i++)
            dc.DrawRoundedRectangle(brush, null, new Rect(x0 + (i % 2) * (q + gap), y0 + (i / 2) * (q + gap), q, q), s * 0.04, s * 0.04);
    }
}
