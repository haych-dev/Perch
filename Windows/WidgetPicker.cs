using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Perch.Core;
using Perch.Widgets;

namespace Perch;

/// <summary>Lists every widget by category; switch one on to add it to the end of the dock.</summary>
public sealed class WidgetPicker : PerchWindow
{
    static WidgetPicker? _open;
    readonly StackPanel _page = new() { Margin = new Thickness(28, 12, 28, 28) };
    string _filter = "";

    public static void ShowSingle()
    {
        if (_open != null) { _open.Activate(); return; }
        _open = new WidgetPicker();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
    }

    WidgetPicker()
    {
        Title = "Perch widgets";
        Width = 560;
        Height = 760;
        MinWidth = 440;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/perch.ico"));
        Content = new ScrollViewer { Content = _page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Build();
    }

    void Build()
    {
        _page.Children.Clear();
        var title = Ui.Text("Widgets", 26, FontWeights.SemiBold);
        title.FontFamily = (FontFamily)Application.Current.Resources["F.Display"];
        _page.Children.Add(title);
        _page.Children.Add(Ui.Wrap(Ui.Dim("Switch a widget on to add it to the dock. Drag tiles on the dock to reorder them, or right-click one to remove it.", 12.5)));
        var search = Ui.Field(out var box, _filter, "Search widgets");
        search.Margin = new Thickness(0, 14, 0, 0);
        box.TextChanged += (_, _) => { _filter = box.Text; Fill(); };
        _page.Children.Add(search);
        _list = new StackPanel();
        _page.Children.Add(_list);
        Fill();
    }

    StackPanel _list = new();

    void Fill()
    {
        _list.Children.Clear();
        var items = Settings.Current.Items;
        var all = WidgetRegistry.Catalogue();

        bool Match(string name, string desc) => _filter.Length == 0 || name.Contains(_filter, StringComparison.OrdinalIgnoreCase) || desc.Contains(_filter, StringComparison.OrdinalIgnoreCase);

        foreach (var cat in WidgetRegistry.Categories)
        {
            var rows = new List<UIElement>();
            if (cat == "Essentials" && Match("Apps", "pinned and open apps"))
                rows.Add(Row("Apps", "Pinned apps and open windows, like the taskbar.", "", items.Contains("apps"), on => Toggle("apps", on)));
            foreach (var w in all.Where(w => w.Category == cat && Match(w.Name, w.Description)))
            {
                var id = w.Id;
                if (id == "sep")
                    rows.Add(Ui.Line(RowText(w.Name, w.Description, w.Glyph), Ui.Button("Add", () => Settings.Update(s => s.Items.Add("sep"), rebuild: true))));
                else
                    rows.Add(Row(w.Name, w.Description, w.Glyph, items.Contains(id), on => Toggle(id, on)));
            }
            if (rows.Count == 0) continue;
            _list.Children.Add(Section(cat.ToUpperInvariant()));
            _list.Children.Add(Group(rows.ToArray()));
        }
    }

    static UIElement RowText(string name, string desc, string glyph) =>
        Ui.Col(2, Ui.Text(name, 13.5, FontWeights.SemiBold), Ui.Wrap(Ui.Dim(desc, 12)));

    static Grid Row(string name, string desc, string glyph, bool on, Action<bool> toggled)
    {
        var text = RowText(name, desc, glyph);
        ((FrameworkElement)text).Margin = new Thickness(0, 0, 16, 0);
        return Ui.Line(text, Ui.Switch(on, toggled));
    }

    static void Toggle(string id, bool on)
    {
        Settings.Update(s =>
        {
            if (on && !s.Items.Contains(id))
            {
                // New widgets go before the clock if there is one, otherwise at the end.
                int clock = s.Items.IndexOf("clock");
                if (id == "apps" || id == "start") s.Items.Insert(0, id);
                else if (clock >= 0) s.Items.Insert(clock, id);
                else s.Items.Add(id);
            }
            else if (!on) s.Items.Remove(id);
        }, rebuild: true);
    }
}
