using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Perch.Core;

namespace Perch.Widgets;

/// <summary>
/// Quick launch: one tile that opens a grid of pinned apps. While it is on the dock, pinned apps live here
/// and the apps group only shows what is open, so the dock stays short however many apps are pinned.
/// </summary>
public sealed class LauncherWidget : Widget
{
    public override string Id => "launcher";
    public override string Name => "Quick launch";
    public override string Category => "Essentials";
    public override string Description => "Your pinned apps in one tile. While it's on the dock, the apps group shows only open apps.";
    public override bool IsIcon => true;
    public override bool AcceptsFiles => true;
    public override double CardWidth => 392;

    bool _adding;
    string _filter = "";

    protected override FrameworkElement BuildTile() => IconFrame(TileGlyph("", 18));

    public override void OnCardClosed() { _adding = false; _filter = ""; }

    public override FrameworkElement? BuildCard() => _adding ? AddView() : GridView();

    FrameworkElement GridView()
    {
        var pins = Settings.Current.Pins;
        var add = Ui.Button("Add apps", () => { _adding = true; Host.RefreshCard(this); });
        var head = Ui.Line(Ui.Title("Quick launch"), add);
        if (pins.Count == 0)
        {
            return Ui.Col(10, head, Ui.Wrap(Ui.Dim("Nothing here yet. Add apps from the list, or drag apps and shortcuts onto this tile.", 12.5)));
        }
        var grid = new UniformGrid { Columns = 4 };
        foreach (var pin in pins.ToList())
        {
            var p = pin;
            var icon = new Image { Width = 40, Height = 40, Source = ShellIcons.Get(Apps.IconSource(p)), HorizontalAlignment = HorizontalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            var label = Ui.Text(p.Name, 11.5);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.TextAlignment = TextAlignment.Center;
            label.MaxWidth = 76;
            var cell = Ui.ListRow(Ui.Col(6, icon, label), () => { Apps.Launch(p); Host.Bounce(Tile); Host.CloseCard(); }, () => PinMenu(p));
            cell.Padding = new Thickness(4, 8, 4, 8);
            cell.ToolTip = p.Name;
            grid.Children.Add(cell);
        }
        return Ui.Col(10, head, Ui.Scroll(grid, 360), Ui.Dim("Right-click an app to move or remove it.", 11.5));
    }

    void PinMenu(PinnedApp p)
    {
        var pins = Settings.Current.Pins;
        int i = pins.IndexOf(p);
        var menu = new ContextMenu();
        void Move(int to)
        {
            Settings.Update(s => { s.Pins.Remove(p); s.Pins.Insert(Math.Clamp(to, 0, s.Pins.Count), p); });
            Host.RefreshCard(this);
        }
        menu.Items.Add(new MenuItem { Header = "Move earlier", Tag = "", IsEnabled = i > 0 }.With(() => Move(i - 1)));
        menu.Items.Add(new MenuItem { Header = "Move later", Tag = "", IsEnabled = i < pins.Count - 1 }.With(() => Move(i + 1)));
        var location = p.Target ?? (p.Launch.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? p.Launch : null);
        if (location != null && File.Exists(location))
            menu.Items.Add(new MenuItem { Header = "Open file location", Tag = "" }.With(() => Apps.OpenFileLocation(location)));
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Remove from Quick launch", Tag = "" }.With(() => { Apps.Unpin(p); Host.RefreshCard(this); }));
        menu.IsOpen = true;
    }

    FrameworkElement AddView()
    {
        var list = new StackPanel();
        var field = Ui.Field(out var box, _filter, "Find an app");
        int generation = 0;
        void Fill()
        {
            list.Children.Clear();
            int gen = ++generation;
            var q = box.Text.Trim();
            // Start's full "All apps" list, so Store apps such as Notepad and Calculator are here too.
            var apps = Apps.AllApps().Where(a => q.Length == 0 || a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(60).ToList();
            foreach (var (name, id) in apps)
            {
                var pinned = Settings.Current.Pins.FirstOrDefault(p => Apps.IsSameApp(p, id));
                var icon = new Image { Width = 24, Height = 24 };
                // Icons are filled in after the list shows, so the list appears straight away.
                var source = id.Contains('\\') && !id.StartsWith('{') ? id : "shell:AppsFolder\\" + id;
                icon.Dispatcher.BeginInvoke(() => { if (gen == generation) icon.Source = ShellIcons.Get(source); }, System.Windows.Threading.DispatcherPriority.Background);
                var label = Ui.Text(name, 13);
                label.MaxWidth = 260;
                var mark = Ui.Glyph(pinned != null ? "" : "", 13, pinned != null ? "Accent" : "Card.Dim");
                string n = name, i = id;
                list.Children.Add(Ui.ListRow(Ui.Line(Ui.Row(10, icon, label), mark), () =>
                {
                    var existing = Settings.Current.Pins.FirstOrDefault(x => Apps.IsSameApp(x, i));
                    if (existing != null) Apps.Unpin(existing);
                    else Apps.Pin(Apps.FromAllApps(n, i));
                    Fill();
                }));
            }
            if (apps.Count == 0) list.Children.Add(Ui.Dim("No apps match.", 12.5));
        }
        box.TextChanged += (_, _) => { _filter = box.Text; Fill(); };
        box.Loaded += (_, _) => { box.Focus(); box.CaretIndex = box.Text.Length; };
        Fill();
        var done = Ui.Button("Done", () => { _adding = false; _filter = ""; Host.RefreshCard(this); }, "Primary");
        return Ui.Col(10, Ui.Line(Ui.Title("Add to Quick launch"), done), field, Ui.Scroll(list, 340));
    }

    public override void Drop(string[] files)
    {
        foreach (var f in files)
            if (Apps.FromFile(f) is { } pin) Apps.Pin(pin);
        Host.Bounce(Tile);
        if (CardOpen) Host.RefreshCard(this);
    }

    public override void AddMenuItems(ContextMenu menu)
    {
        menu.Items.Add(new MenuItem { Header = "Add apps…", Tag = "" }.With(() => { _adding = true; Host.OpenCard(this); }));
    }
}
