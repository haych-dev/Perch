using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Perch.Core;

namespace Perch.Widgets;

/// <summary>Claude Code usage from its history on this PC: the current 5-hour window, the week, and what's running.</summary>
public sealed class ClaudeCodeWidget : Widget
{
    public override string Id => "claudecode";
    public override string Name => "Claude Code";
    public override string Category => "AI";
    public override string Description => "Your Claude Code usage: this 5-hour window and when it ends, the last 7 days, your most-used model and sessions working now. Read from Claude Code's history on this PC.";
    public override double CardWidth => 340;

    static readonly Color Orange = Color.FromRgb(0xD9, 0x77, 0x57);
    static readonly SolidColorBrush OrangeBrush = Frozen(new SolidColorBrush(Orange));
    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    TextBlock? _main, _sub;

    public ClaudeCodeWidget()
    {
        ClaudeUsage.Instance.Changed += Update;
    }

    public override void Detach()
    {
        ClaudeUsage.Instance.Changed -= Update;
        if (_started) ClaudeUsage.Instance.Stop();
    }

    bool _started;

    protected override FrameworkElement BuildTile()
    {
        _main = TileText("Claude Code", 12.5, FontWeights.SemiBold);
        _sub = TileText("Reading history…", 10, null, "Dock.Dim");
        var mark = new Mark { Width = 20 * Scale, Height = 20 * Scale };
        _started = true;
        ClaudeUsage.Instance.Start();
        Update();
        return Frame(Ui.Row(8 * Scale, mark, Ui.Col(0, _main, _sub)), null, 10);
    }

    int _tick;
    public override void Tick()
    {
        // The window's "ends in" text moves on even when nothing new is written.
        if (++_tick % 30 == 0) Update();
    }

    void Update()
    {
        if (_main == null || _sub == null) return;
        var s = ClaudeUsage.Instance.Current;
        if (s == null) return;
        if (!s.Installed) { _main.Text = "Claude Code"; _sub.Text = "Not used on this PC"; }
        else if (s.Plan is { Session: double five } plan)
        {
            _main.Text = $"{five:0}% of session" + (plan.Week is double wk ? $" · {wk:0}% week" : "");
            _sub.Text = s.Current != null ? $"Resets {Clock(s.Current.End)}" : "No window running";
        }
        else if (s.Current is { } w)
        {
            _main.Text = $"{ClaudeUsage.Short(w.Tokens)} tokens";
            _sub.Text = $"Window ends {Clock(w.End)}";
        }
        else
        {
            _main.Text = "Claude Code";
            _sub.Text = s.WeekTokens > 0 ? $"Idle · {ClaudeUsage.Short(s.WeekTokens)} this week" : "Idle";
        }
        if (CardOpen) Host.RefreshCard(this);
    }

    static string Clock(DateTime t) => ClockWidget.FormatTime(t, false);

    static string Left(TimeSpan t) => t.TotalMinutes < 60 ? $"{Math.Max(1, (int)t.TotalMinutes)} min" : $"{(int)t.TotalHours} h {t.Minutes} min";

    public override FrameworkElement? BuildCard()
    {
        var s = ClaudeUsage.Instance.Current;
        var header = Ui.Row(8, new Mark { Width = 18, Height = 18 }, Ui.Title("Claude Code"));
        if (s == null) return Ui.Col(12, header, Ui.Dim("Reading Claude Code's history…", 12.5));
        if (!s.Installed)
            return Ui.Col(12, header, Ui.Wrap(Ui.Dim("Claude Code hasn't been used on this PC yet. Once it has, this shows your usage from its history.", 12.5)));

        var col = Ui.Col(12, header);

        if (s.Plan is { } plan)
        {
            var limits = Ui.Col(8);
            if (plan.Session is double five) limits.Children.Add(Limit("Session (5 hours)", five));
            if (plan.Week is double wk) limits.Children.Add(Limit("Week", wk));
            col.Children.Add(limits);
        }

        // This 5-hour window.
        if (s.Current is { } w)
        {
            var now = DateTime.Now;
            double elapsed = (now - w.Start).TotalMinutes / (w.End - w.Start).TotalMinutes * 100;
            col.Children.Add(Ui.Col(6,
                Ui.Line(Ui.Text("This 5-hour window", 13, FontWeights.SemiBold), Ui.Dim($"ends {Clock(w.End)}, in {Left(w.End - now)}", 12)),
                Ui.Bar(elapsed, "Accent", 6),
                Ui.Line(Ui.Dim($"Since {Clock(w.Start)}", 12), Ui.Dim($"{w.Replies:N0} replies", 12)),
                Ui.Line(Ui.Text("Tokens used", 13), Ui.Text(ClaudeUsage.Short(w.Tokens), 13, FontWeights.SemiBold)),
                Ui.Line(Ui.Dim("Re-read from cache", 12), Ui.Dim(ClaudeUsage.Short(w.CacheRead), 12))));
        }
        else col.Children.Add(Ui.Wrap(Ui.Dim("No 5-hour window is running. One starts with your next message to Claude Code.", 12.5)));

        col.Children.Add(Ui.Divider());

        // The last 7 days.
        var bars = new DayBars { Values = s.Days, Height = 64 };
        var week = Ui.Col(8,
            Ui.Line(Ui.Text("Last 7 days", 13, FontWeights.SemiBold), Ui.Dim($"{ClaudeUsage.Short(s.WeekTokens)} tokens", 12)),
            bars,
            Ui.Line(Ui.Dim("Replies", 12), Ui.Text(s.WeekReplies.ToString("N0", CultureInfo.CurrentCulture), 12.5)),
            Ui.Line(Ui.Dim("Sessions", 12), Ui.Text(s.WeekSessions.ToString("N0", CultureInfo.CurrentCulture), 12.5)));
        if (s.Models.Count > 0)
        {
            var top = s.Models[0];
            week.Children.Add(Ui.Line(Ui.Dim("Most used model", 12), Ui.Text($"{top.Model} · {top.Share * 100:0}%", 12.5, FontWeights.SemiBold)));
        }
        col.Children.Add(week);

        // Sessions with replies in the last 10 minutes.
        if (s.Active.Count > 0)
        {
            col.Children.Add(Ui.Divider());
            var list = Ui.Col(4, Ui.Text("Working now", 13, FontWeights.SemiBold));
            foreach (var (project, last) in s.Active.Take(5))
            {
                var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), VerticalAlignment = VerticalAlignment.Center };
                dot.SetResourceReference(Border.BackgroundProperty, "Good");
                var name = Ui.Text(project.Length > 0 ? project : "Session", 12.5);
                name.MaxWidth = 200;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                list.Children.Add(Ui.Line(Ui.Row(8, dot, name), Ui.Dim(DownloadsWidget.Ago(last), 12)));
            }
            col.Children.Add(list);
        }

        var note = Ui.Wrap(Ui.Dim(s.Plan != null
            ? "Limits come from your Claude Code status line. Tokens are read from Claude Code's history on this PC, and nothing is sent anywhere."
            : "Read from Claude Code's history on this PC, and nothing is sent anywhere. The 5-hour window is estimated from when you started.", 10.5));
        note.Margin = new Thickness(0, 4, 0, 0);
        col.Children.Add(note);
        return col;
    }

    static FrameworkElement Limit(string label, double pct)
    {
        string brush = pct >= 90 ? "Bad" : pct >= 70 ? "Warn" : "Accent";
        return Ui.Col(4, Ui.Line(Ui.Text(label, 12.5), Ui.Text($"{pct:0}% used", 12.5, FontWeights.SemiBold)), Ui.Bar(pct, brush, 6));
    }

    /// <summary>A small sunburst in Claude's orange.</summary>
    sealed class Mark : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0) return;
            var c = new Point(w / 2, h / 2);
            double r = Math.Min(w, h) / 2;
            var pen = Frozen(new Pen(OrangeBrush, Math.Max(1.4, r * 0.2)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
            for (int i = 0; i < 10; i++)
            {
                double a = i * Math.PI / 5 + 0.15;
                double len = i % 2 == 0 ? r * 0.92 : r * 0.68;
                dc.DrawLine(pen, new Point(c.X + Math.Cos(a) * r * 0.12, c.Y + Math.Sin(a) * r * 0.12), new Point(c.X + Math.Cos(a) * len, c.Y + Math.Sin(a) * len));
            }
        }
    }

    /// <summary>Seven bars, oldest on the left, with weekday letters under them.</summary>
    sealed class DayBars : FrameworkElement
    {
        public long[] Values = [];

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || Values.Length == 0) return;
            const double labelH = 14;
            double max = Math.Max(1, Values.Max());
            double slot = w / Values.Length, barW = Math.Min(26, slot * 0.6);
            var dim = Theme.Brush("Card.Dim");
            var track = Theme.Brush("Card.Track");
            var accent = Theme.Brush("Accent");
            var typeface = new Typeface((FontFamily)Application.Current.Resources["F.Ui"], FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            for (int i = 0; i < Values.Length; i++)
            {
                double x = i * slot + (slot - barW) / 2, full = h - labelH - 2;
                dc.DrawRoundedRectangle(track, null, new Rect(x, 0, barW, full), 4, 4);
                if (Values[i] > 0)
                {
                    double bh = Math.Max(4, full * Values[i] / max);
                    dc.DrawRoundedRectangle(i == Values.Length - 1 ? accent : Theme.Brush("AccentSoft"), null, new Rect(x, full - bh, barW, bh), 4, 4);
                }
                var day = DateTime.Today.AddDays(i - (Values.Length - 1));
                var text = new FormattedText(day.ToString("ddd", CultureInfo.CurrentCulture)[..1], CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 10.5, dim, dpi);
                dc.DrawText(text, new Point(i * slot + (slot - text.Width) / 2, h - labelH + 1));
            }
        }
    }
}
