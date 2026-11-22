using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Perch.Core;
using Windows.Devices.Radios;

namespace Perch.Widgets;

public sealed class TogglesWidget : Widget
{
    public override string Id => "toggles";
    public override string Name => "Quick settings";
    public override string Category => "Essentials";
    public override string Description => "Wi-Fi, Bluetooth, dark mode and brightness in one place.";
    public override bool IsIcon => true;
    public override double CardWidth => 320;

    protected override FrameworkElement BuildTile() => IconFrame(TileGlyph("", 18));

    public override FrameworkElement? BuildCard()
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        var col = Ui.Col(14, Ui.Title("Quick settings"), grid);
        _ = AddRadio(grid, RadioKind.WiFi, "Wi-Fi", "", "network-wifi");
        _ = AddRadio(grid, RadioKind.Bluetooth, "Bluetooth", "", "bluetooth");
        grid.Children.Add(QuickTile("Dark mode", "", QuickSettings.IsDarkMode(), on => QuickSettings.SetDarkMode(on), null));
        grid.Children.Add(QuickTile("Night light", "", false, _ => QuickSettings.OpenSettings("nightlight"), null, action: true));
        grid.Children.Add(QuickTile("Focus", "", false, _ => QuickSettings.OpenSettings("quiethours"), null, action: true));
        grid.Children.Add(QuickTile("Project", "", false, _ => Apps.Open("ms-settings-displays-topology:"), null, action: true));

        int b = QuickSettings.GetBrightness();
        if (b >= 0)
        {
            var slider = Ui.Slider(0, 100, b, v => QuickSettings.SetBrightness((int)v), 240);
            col.Children.Add(Ui.Row(8, Ui.Glyph("", 15, "Card.Dim"), slider));
        }
        col.Children.Add(Ui.Row(8,
            Ui.Button("All settings", () => { QuickSettings.OpenSettings(""); Host.CloseCard(); }),
            Ui.Button("Display", () => { QuickSettings.OpenSettings("display"); Host.CloseCard(); })));
        return col;
    }

    async Task AddRadio(Panel grid, RadioKind kind, string label, string glyph, string page)
    {
        var placeholder = QuickTile(label, glyph, false, _ => QuickSettings.OpenSettings(page), null, action: true);
        grid.Children.Insert(Math.Min(grid.Children.Count, kind == RadioKind.WiFi ? 0 : 1), placeholder);
        var radio = await QuickSettings.GetRadioAsync(kind);
        if (radio == null) return;
        int i = grid.Children.IndexOf(placeholder);
        if (i < 0) return;
        grid.Children.RemoveAt(i);
        grid.Children.Insert(i, QuickTile(label, glyph, radio.State == RadioState.On, on => _ = QuickSettings.SetRadioAsync(radio, on), () => QuickSettings.OpenSettings(page)));
    }

    static Border QuickTile(string label, string glyph, bool on, Action<bool> toggled, Action? more, bool action = false)
    {
        var g = Ui.Glyph(glyph, 18, on ? "OnAccent" : "Card.Text");
        g.HorizontalAlignment = HorizontalAlignment.Left;
        var t = Ui.Text(label, 12, FontWeights.SemiBold, on ? "OnAccent" : "Card.Text");
        var b = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 10, 10), Margin = new Thickness(3), Cursor = Cursors.Hand, Child = Ui.Col(8, g, t), Height = 74 };
        void Paint()
        {
            b.SetResourceReference(Border.BackgroundProperty, on ? "Accent" : "Card.Field");
            g.SetResourceReference(TextBlock.ForegroundProperty, on ? "OnAccent" : "Card.Text");
            t.SetResourceReference(TextBlock.ForegroundProperty, on ? "OnAccent" : "Card.Text");
        }
        Paint();
        b.MouseLeftButtonUp += (_, e) =>
        {
            if (!action) { on = !on; Paint(); }
            toggled(on);
            e.Handled = true;
        };
        if (more != null) b.MouseRightButtonUp += (_, e) => { more(); e.Handled = true; };
        return b;
    }
}

public sealed class SearchWidget : Widget
{
    public override string Id => "search";
    public override string Name => "Search";
    public override string Category => "Essentials";
    public override string Description => "Find apps, open folders and paths, or search the web.";
    public override double CardWidth => 360;

    TextBox? _box;
    StackPanel? _results;
    int _selected;
    readonly List<(Action Run, Border Row)> _rows = [];

    protected override FrameworkElement BuildTile()
    {
        var row = Ui.Row(8 * Scale, TileGlyph("", 14, "Dock.Dim"), TileText("Search", 12.5, null, "Dock.Dim"));
        return Frame(row, 120 * Scale, 12);
    }

    public override FrameworkElement? BuildCard()
    {
        var field = Ui.Field(out _box, "", "Type an app, a path, or a question");
        _box.FontSize = 14;
        _box.Padding = new Thickness(10, 8, 10, 8);
        _results = new StackPanel();
        _box.TextChanged += (_, _) => Fill();
        _box.PreviewKeyDown += OnKey;
        _box.Loaded += (_, _) => { _box.Focus(); Keyboard.Focus(_box); };
        Fill();
        return Ui.Col(10, field, _results);
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down) { _selected = Math.Min(_rows.Count - 1, _selected + 1); Highlight(); e.Handled = true; }
        else if (e.Key == Key.Up) { _selected = Math.Max(0, _selected - 1); Highlight(); e.Handled = true; }
        else if (e.Key == Key.Enter && _rows.Count > 0) { _rows[Math.Clamp(_selected, 0, _rows.Count - 1)].Run(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Host.CloseCard(); e.Handled = true; }
    }

    void Highlight()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (i == _selected) _rows[i].Row.SetResourceReference(Border.BackgroundProperty, "Card.Hover");
            else _rows[i].Row.Background = Brushes.Transparent;
        }
    }

    void Fill()
    {
        if (_results == null || _box == null) return;
        _results.Children.Clear();
        _rows.Clear();
        _selected = 0;
        string q = _box.Text.Trim();

        if (q.Length == 0)
        {
            _results.Children.Add(Ui.Dim("Recently added apps and your pins show up as you type.", 12));
            return;
        }

        // Paths and URLs first.
        string expanded = Environment.ExpandEnvironmentVariables(q);
        if ((expanded.Length >= 2 && expanded[1] == ':' || expanded.StartsWith("\\\\")) && (Directory.Exists(expanded) || File.Exists(expanded)))
            Add("", $"Open {expanded}", "Location", () => Apps.Open(expanded), ShellIcons.Get(expanded));
        if (Uri.TryCreate(q.Contains("://") ? q : "https://" + q, UriKind.Absolute, out var uri) && q.Contains('.') && !q.Contains(' '))
            Add("", $"Go to {uri.Host}", "Website", () => Apps.Open(uri.ToString()));

        foreach (var (name, path) in Apps.StartMenuApps()
                     .Select(a => (a.Name, a.Path, Score: Score(a.Name, q)))
                     .Where(a => a.Score > 0)
                     .OrderByDescending(a => a.Score).ThenBy(a => a.Name.Length)
                     .Take(6)
                     .Select(a => (a.Name, a.Path)))
        {
            var p = path;
            Add(null, name, "App", () => Apps.Launch(p), ShellIcons.Get(p));
        }

        string engine = Settings.Current.SearchEngine;
        string url = engine switch
        {
            "bing" => "https://www.bing.com/search?q=",
            "duckduckgo" => "https://duckduckgo.com/?q=",
            _ => "https://www.google.com/search?q=",
        } + Uri.EscapeDataString(q);
        string engineName = engine switch { "bing" => "Bing", "duckduckgo" => "DuckDuckGo", _ => "Google" };
        Add("", $"Search {engineName} for “{q}”", "Web", () => Apps.Open(url));
        Highlight();
    }

    static int Score(string name, string q)
    {
        if (name.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 3;
        if (name.Split(' ', '-', '.').Any(w => w.StartsWith(q, StringComparison.OrdinalIgnoreCase))) return 2;
        if (name.Contains(q, StringComparison.OrdinalIgnoreCase)) return 1;
        return 0;
    }

    void Add(string? glyph, string text, string kind, Action run, ImageSource? icon = null)
    {
        UIElement lead = icon != null
            ? new Image { Source = icon, Width = 24, Height = 24 }
            : new Border { Width = 24, Child = Ui.Glyph(glyph ?? "", 15, "Card.Dim") };
        var label = Ui.Text(text, 13);
        label.MaxWidth = 250;
        var line = Ui.Line(Ui.Row(10, lead, label), Ui.Dim(kind, 11));
        void Go() { run(); Host.CloseCard(); }
        var row = Ui.ListRow(line, Go);
        _rows.Add((Go, row));
        _results!.Children.Add(row);
    }
}

public sealed class TodoWidget : Widget
{
    public override string Id => "todos";
    public override string Name => "To-dos";
    public override string Category => "Productivity";
    public override string Description => "A short list of things to get done. Kept on this PC.";
    public override double CardWidth => 320;

    TextBlock? _count;
    StackPanel? _list;

    protected override FrameworkElement BuildTile()
    {
        _count = TileText("", 13, FontWeights.SemiBold);
        Tick();
        return Frame(Ui.Row(7 * Scale, TileGlyph("", 15), _count), null, 10);
    }

    public override void Tick()
    {
        int open = Settings.Current.Todos.Count(t => !t.Done);
        if (_count != null) _count.Text = open == 0 ? "All done" : $"{open} to do";
    }

    public override FrameworkElement? BuildCard()
    {
        _list = new StackPanel();
        var field = Ui.Field(out var box, "", "Add a to-do and press Enter");
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || box.Text.Trim().Length == 0) return;
            Settings.Current.Todos.Insert(0, new TodoItem { Text = box.Text.Trim() });
            Settings.SaveSoon();
            box.Text = "";
            Fill();
            Tick();
        };
        box.Loaded += (_, _) => box.Focus();
        Fill();
        var clear = Ui.Button("Clear done", () => { Settings.Current.Todos.RemoveAll(t => t.Done); Settings.SaveSoon(); Fill(); }, "Flat");
        return Ui.Col(10, Ui.Line(Ui.Title("To-dos"), clear), field, Ui.Scroll(_list, 320));
    }

    void Fill()
    {
        if (_list == null) return;
        _list.Children.Clear();
        var todos = Settings.Current.Todos;
        foreach (var t in todos.OrderBy(t => t.Done).ToList())
        {
            var item = t;
            var check = new CheckBox { IsChecked = t.Done, Style = (Style)Application.Current.Resources["Check"], VerticalAlignment = VerticalAlignment.Center };
            var text = Ui.Text(t.Text, 13, null, t.Done ? "Card.Dim" : "Card.Text");
            text.TextWrapping = TextWrapping.Wrap;
            text.TextTrimming = TextTrimming.None;
            text.MaxWidth = 220;
            if (t.Done) text.TextDecorations = TextDecorations.Strikethrough;
            check.Click += (_, _) => { item.Done = check.IsChecked == true; Settings.SaveSoon(); Fill(); Tick(); };
            var del = Ui.IconButton("", () => { todos.Remove(item); Settings.SaveSoon(); Fill(); Tick(); }, 9);
            del.Opacity = 0.6;
            _list.Children.Add(Ui.ListRow(Ui.Line(Ui.Row(10, check, text), del), null));
        }
        if (todos.Count == 0) _list.Children.Add(Ui.Dim("Nothing on the list.", 12.5));
    }
}

public sealed class NotesWidget : Widget
{
    public override string Id => "notes";
    public override string Name => "Notes";
    public override string Category => "Productivity";
    public override string Description => "A scratch pad that saves as you type.";
    public override bool IsIcon => true;
    public override double CardWidth => 340;

    protected override FrameworkElement BuildTile() => IconFrame(TileGlyph("", 18));

    public override FrameworkElement? BuildCard()
    {
        var box = new TextBox
        {
            Text = Settings.Current.Notes,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 260,
            FontSize = 13.5,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        box.TextChanged += (_, _) => { Settings.Current.Notes = box.Text; Settings.SaveSoon(); };
        box.Loaded += (_, _) => { box.Focus(); box.CaretIndex = box.Text.Length; };
        var copy = Ui.Button("Copy all", () => { try { Clipboard.SetText(box.Text); } catch { } }, "Flat");
        return Ui.Col(10, Ui.Line(Ui.Title("Notes"), copy), box);
    }
}
