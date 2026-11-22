using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Perch.Core;

namespace Perch;

public sealed class SettingsWindow : PerchWindow
{
    static SettingsWindow? _open;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(140) };
    Action? _pending;

    public static void ShowSingle()
    {
        if (_open != null) { _open.Activate(); return; }
        _open = new SettingsWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
    }

    SettingsWindow()
    {
        Title = "Perch settings";
        Width = 600;
        Height = 760;
        MinWidth = 480;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/perch.ico"));
        _debounce.Tick += (_, _) => { _debounce.Stop(); _pending?.Invoke(); _pending = null; };
        Build();
    }

    /// <summary>Slider changes rebuild the dock, so they are batched.</summary>
    void Later(Action a) { _pending = a; _debounce.Stop(); _debounce.Start(); }

    static void Change(Action<Settings> a, bool rebuild = true) => Settings.Update(a, rebuild);

    void Build()
    {
        var s = Settings.Current;
        var page = new StackPanel { Margin = new Thickness(28, 12, 28, 28) };

        var title = Ui.Text("Settings", 26, FontWeights.SemiBold);
        title.FontFamily = (FontFamily)Application.Current.Resources["F.Display"];
        page.Children.Add(title);

        // ── Look ──
        page.Children.Add(Section("LOOK"));
        var swatches = new WrapPanel();
        foreach (var hex in Theme.Accents)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(c), Cursor = Cursors.Hand, BorderThickness = new Thickness(2) };
            dot.BorderBrush = hex.Equals(s.Accent, StringComparison.OrdinalIgnoreCase) ? Theme.Brush("Card.Text") : Brushes.Transparent;
            var h = hex;
            dot.MouseLeftButtonUp += (_, _) =>
            {
                Change(x => x.Accent = h, false);
                foreach (Border d in swatches.Children) d.BorderBrush = Brushes.Transparent;
                dot.BorderBrush = Theme.Brush("Card.Text");
            };
            swatches.Children.Add(dot);
        }
        page.Children.Add(Group(
            Ui.Col(8, Ui.Text("Style", 13), Ui.Segmented(Theme.Styles.Select(x => (x.Id, x.Name.Replace(" glass", ""))), s.Style, v => Change(x => x.Style = v))),
            Ui.Line(Ui.Text("Accent colour", 13), swatches),
            Ui.Line(Ui.Col(2, Ui.Text("Icon size", 13), Ui.Dim("Also sets the height of the dock", 11.5)), Ui.Slider(28, 80, s.IconSize, v => Later(() => Change(x => x.IconSize = (int)v)))),
            Ui.SwitchLine("Magnify on hover", "Icons grow as the pointer passes over them", s.Magnify, v => Change(x => x.Magnify = v)),
            Ui.Line(Ui.Text("Magnification", 13), Ui.Slider(1.1, 2.0, s.MagnifyScale, v => Later(() => Change(x => x.MagnifyScale = Math.Round(v, 2))))),
            Ui.SwitchLine("Full width", "Stretch the dock from edge to edge", s.FullWidth, v => Change(x => x.FullWidth = v)),
            Ui.Line(Ui.Text("Corner radius", 13), Ui.Slider(0, 40, s.CornerRadius, v => Later(() => Change(x => x.CornerRadius = (int)v)))),
            Ui.Line(Ui.Text("Spacing", 13), Ui.Slider(0, 20, s.Spacing, v => Later(() => Change(x => x.Spacing = (int)v)))),
            Ui.Line(Ui.Text("Padding", 13), Ui.Slider(3, 16, s.Padding, v => Later(() => Change(x => x.Padding = (int)v)))),
            Ui.Line(Ui.Text("Distance from edge", 13), Ui.Slider(0, 32, s.EdgeGap, v => Later(() => Change(x => x.EdgeGap = (int)v)))),
            Ui.SwitchLine("Show names on hover", null, s.ShowLabels, v => Change(x => x.ShowLabels = v, false)),
            Ui.Line(Ui.Text("Open app indicator", 13), Ui.Segmented(new[] { ("dot", "Dot"), ("line", "Line") }, s.Indicator, v => Change(x => x.Indicator = v, false)))));

        // ── Position ──
        page.Children.Add(Section("POSITION AND BEHAVIOUR"));
        var monitors = Monitors.All();
        page.Children.Add(Group(
            Ui.Line(Ui.Text("Screen edge", 13), Ui.Segmented(new[] { ("bottom", "Bottom"), ("top", "Top") }, s.Edge, v => Change(x => x.Edge = v))),
            monitors.Count > 1
                ? Ui.Col(8, Ui.Text("Display", 13), Ui.Segmented(monitors.Select((m, i) => (i, m.Label(i))), Math.Min(s.Monitor, monitors.Count - 1), v => Change(x => x.Monitor = v)))
                : Ui.Line(Ui.Text("Display", 13), Ui.Dim(monitors.FirstOrDefault()?.Label(0) ?? "")),
            Ui.SwitchLine("Auto-hide", "Slide away until the pointer touches the screen edge", s.AutoHide, v => Change(x => x.AutoHide = v)),
            Ui.SwitchLine("Hide the Windows taskbar", "Put back as soon as Perch quits", s.HideTaskbar, v =>
            {
                Change(x => x.HideTaskbar = v);
                if (v) Taskbar.Hide(); else Taskbar.Restore();
            }),
            Ui.SwitchLine("Hide over full-screen apps", "Games and videos get the whole screen", s.HideInFullscreen, v => Change(x => x.HideInFullscreen = v, false)),
            Ui.SwitchLine("Lock dock", "Stop items being dragged around", s.Locked, v => Change(x => x.Locked = v, false)),
            Ui.SwitchLine("Start with Windows", null, QuickSettings.IsStartupEnabled(), v => { QuickSettings.SetStartup(v); Change(x => x.StartWithWindows = v, false); })));

        // ── Clock and search ──
        page.Children.Add(Section("CLOCK AND SEARCH"));
        page.Children.Add(Group(
            Ui.SwitchLine("24-hour clock", null, s.Clock24h, v => Change(x => x.Clock24h = v)),
            Ui.SwitchLine("Show seconds", null, s.ClockSeconds, v => Change(x => x.ClockSeconds = v)),
            Ui.SwitchLine("Show the date under the time", null, s.ClockDate, v => Change(x => x.ClockDate = v)),
            Ui.Line(Ui.Text("Web search", 13), Ui.Segmented(new[] { ("google", "Google"), ("bing", "Bing"), ("duckduckgo", "DuckDuckGo") }, s.SearchEngine, v => Change(x => x.SearchEngine = v, false))),
            Ui.Line(Ui.Text("Temperature", 13), Ui.Segmented(new[] { (false, "°C"), (true, "°F") }, s.Fahrenheit, v => { Change(x => x.Fahrenheit = v, false); _ = Weather.Instance.RefreshAsync(); }))));

        // ── Dock contents ──
        page.Children.Add(Section("DOCK CONTENTS"));
        page.Children.Add(Group(
            Ui.Line(Ui.Col(2, Ui.Text("Widgets", 13), Ui.Dim("Add, remove and reorder what's on the dock", 11.5)), Ui.Button("Choose widgets…", WidgetPicker.ShowSingle)),
            Ui.Line(Ui.Col(2, Ui.Text("Pinned apps", 13), Ui.Dim($"{s.Pins.Count} pinned. Drag files or shortcuts onto the dock to add more.", 11.5)),
                Ui.Button("Re-import taskbar pins", () => { Settings.Current.PinsImported = false; Apps.ImportTaskbarPins(); Change(_ => { }, false); }))));

        // ── About ──
        page.Children.Add(Section("ABOUT"));
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "";
        page.Children.Add(Group(
            Ui.Line(Ui.Col(2, Ui.Text($"Perch {version}", 13), Ui.Dim("Settings, notes and to-dos are kept in your AppData folder", 11.5)), Ui.Button("Open folder", () => Apps.Open(Settings.Folder))),
            Ui.Line(Ui.Col(2, Ui.Text("Quit Perch", 13), Ui.Dim("The Windows taskbar comes back straight away", 11.5)), Ui.Button("Quit", () => Application.Current.Shutdown()))));

        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
