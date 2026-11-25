using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Perch.Core;
using Perch.Dock;

namespace Perch.Widgets;

/// <summary>Shared base: listens for new forecasts and opens the full weather card on click.</summary>
public abstract class WeatherTileBase : Widget
{
    public override string Category => "Weather";
    public override double CardWidth => 340;
    readonly WeatherWidget _full = new();
    protected StackPanel? Row;

    protected WeatherTileBase() { Weather.Instance.Changed += Refresh; }
    public override void Detach() { Weather.Instance.Changed -= Refresh; _full.Detach(); }

    protected override FrameworkElement BuildTile()
    {
        Row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Refresh();
        return Frame(Row, null, 10);
    }

    void Refresh()
    {
        if (Row == null) return;
        Row.Children.Clear();
        var w = Weather.Instance.Current;
        if (w == null) { Row.Children.Add(Ui.Col(0, TileText(Name, 12.5, FontWeights.SemiBold), TileText(Weather.Instance.HasPlace ? "Loading" : "Set a place", 10, null, "Dock.Dim"))); return; }
        Fill(w);
        if (CardOpen) Host.RefreshCard(this);
    }

    protected abstract void Fill(WeatherNow w);

    protected static WeatherIcon Icon(int code, bool day, double size) => new() { Kind = Weather.KindOf(code), Day = day, Width = size * Scale, Height = size * Scale };

    protected static FrameworkElement Cell(string top, UIElement mid, string bottom, double gapLeft)
    {
        var a = TileText(top, 9.5, null, "Dock.Dim");
        var b = TileText(bottom, 11.5, FontWeights.SemiBold);
        a.HorizontalAlignment = b.HorizontalAlignment = HorizontalAlignment.Center;
        if (mid is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Center;
        var col = new StackPanel { Children = { a, mid, b }, Margin = new Thickness(gapLeft, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        return col;
    }

    public override FrameworkElement? BuildCard() { _full.Host = Host; _full.CardOpen = true; return _full.BuildCard(); }
    public override void OnCardClosed() { _full.CardOpen = false; _full.OnCardClosed(); }
}

public sealed class HourlyWeatherWidget : WeatherTileBase
{
    public override string Id => "hourly";
    public override string Name => "Next hours";
    public override string Description => "Temperature and sky for the next four hours.";

    protected override void Fill(WeatherNow w)
    {
        foreach (var (time, t, code) in w.Hourly.Skip(1).Take(4))
            Row!.Children.Add(Cell(time.ToString(Settings.Current.Clock24h ? "HH:mm" : "h tt").ToLowerInvariant(), Icon(code, time.Hour is > 6 and < 20, 15), $"{t:0}°", Row.Children.Count > 0 ? 10 * Scale : 0));
    }
}

public sealed class WeekWeatherWidget : WeatherTileBase
{
    public override string Id => "week";
    public override string Name => "Next days";
    public override string Description => "Highs, lows and sky for the next three days.";

    protected override void Fill(WeatherNow w)
    {
        foreach (var (day, min, max, code, _) in w.Daily.Skip(1).Take(3))
            Row!.Children.Add(Cell(day.ToString("ddd"), Icon(code, true, 15), $"{max:0}° {min:0}°", Row.Children.Count > 0 ? 12 * Scale : 0));
    }
}

public sealed class SunWidget : WeatherTileBase
{
    public override string Id => "sun";
    public override string Name => "Sunrise & sunset";
    public override string Description => "When the sun comes up and goes down, and how much daylight is left.";

    protected override void Fill(WeatherNow w)
    {
        if (w.Sunrise == default) { Row!.Children.Add(TileText("No sun times", 11.5)); return; }
        // Today's times are in the forecast place's time; show them as given.
        string F(DateTime t) => t.ToString(Settings.Current.Clock24h ? "HH:mm" : "h:mm tt").ToLowerInvariant();
        var up = Ui.Row(4 * Scale, TileGlyph("", 12, "Warn"), TileText(F(w.Sunrise), 12, FontWeights.SemiBold));
        var down = Ui.Row(4 * Scale, TileGlyph("", 11, "Dock.Dim"), TileText(F(w.Sunset), 12, FontWeights.SemiBold));
        var now = DateTime.Now;
        string note = now < w.Sunrise ? "before sunrise" : now < w.Sunset ? $"{(w.Sunset - now).TotalHours:0.0} h of light left" : "after sunset";
        var col = Ui.Col(1, Ui.Row(10 * Scale, up, down), TileText(note, 9.5, null, "Dock.Dim"));
        Row!.Children.Add(col);
    }
}

/// <summary>Today as a line from midnight to midnight, with daylight shaded and a marker for now.</summary>
public sealed class DayTimelineWidget : Widget
{
    public override string Id => "timeline";
    public override string Name => "Day timeline";
    public override string Category => "Time";
    public override string Description => "Today from midnight to midnight, with daylight shaded and where you are now.";
    public override double CardWidth => 340;
    Line? _line;
    TextBlock? _label;

    public DayTimelineWidget() { Weather.Instance.Changed += Redraw; }
    public override void Detach() => Weather.Instance.Changed -= Redraw;
    void Redraw() => _line?.InvalidateVisual();

    protected override FrameworkElement BuildTile()
    {
        _line = new Line { Width = 150 * Scale, Height = 10 * Scale };
        _label = TileText("", 10, null, "Dock.Dim");
        Tick();
        return Frame(Ui.Col(5 * Scale, _line, _label), null, 12);
    }

    public override void Tick()
    {
        _line?.InvalidateVisual();
        if (_label != null)
        {
            var now = DateTime.Now;
            double left = 24 - now.TimeOfDay.TotalHours;
            _label.Text = $"{ClockWidget.FormatTime(now, false)} · {left:0.0} h left today";
        }
    }

    public override FrameworkElement? BuildCard()
    {
        var big = new Line { Height = 22, Labels = true };
        var w = Weather.Instance.Current;
        var col = Ui.Col(12, Ui.Title("Today"), big);
        if (w != null && w.Sunrise != default)
        {
            var light = w.Sunset - w.Sunrise;
            col.Children.Add(Ui.Wrap(Ui.Dim($"Sunrise {ClockWidget.FormatTime(w.Sunrise, false)} · Sunset {ClockWidget.FormatTime(w.Sunset, false)} · {(int)light.TotalHours} h {light.Minutes} min of daylight", 12)));
        }
        else col.Children.Add(Ui.Wrap(Ui.Dim("Set a place in the Weather widget to see daylight on the timeline.", 12)));
        return col;
    }

    sealed class Line : FrameworkElement
    {
        public bool Labels;
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight - (Labels ? 0 : 0);
            if (w <= 0) return;
            double r = h / 2;
            dc.DrawRoundedRectangle(Theme.Brush(Labels ? "Card.Track" : "Tile.Hover"), null, new Rect(0, 0, w, h), r, r);
            var wx = Weather.Instance.Current;
            if (wx != null && wx.Sunrise != default)
            {
                double a = wx.Sunrise.TimeOfDay.TotalHours / 24 * w, b = wx.Sunset.TimeOfDay.TotalHours / 24 * w;
                var sun = Theme.ColorOf("Warn");
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x70, sun.R, sun.G, sun.B)), null, new Rect(a, 0, Math.Max(0, b - a), h), r, r);
            }
            double now = DateTime.Now.TimeOfDay.TotalHours / 24 * w;
            var acc = Theme.ColorOf("Accent");
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x55, acc.R, acc.G, acc.B)), null, new Rect(0, 0, now, h), r, r);
            dc.DrawEllipse(Theme.Brush("Accent"), new Pen(Theme.Brush(Labels ? "Card.Fill" : "Dock.Text"), Math.Max(1, h / 6)), new Point(Math.Clamp(now, r, w - r), h / 2), r * 0.9, r * 0.9);
            for (int hr = 6; hr < 24; hr += 6)
            {
                double x = hr / 24.0 * w;
                dc.DrawLine(new Pen(Theme.Brush(Labels ? "Card.Dim" : "Dock.Dim"), 1) { DashStyle = DashStyles.Dot }, new Point(x, 1), new Point(x, h - 1));
            }
        }
    }
}
