using System.Windows;
using System.Windows.Controls;
using Perch.Core;
using Perch.Dock;

namespace Perch.Widgets;

public sealed class WeatherWidget : Widget
{
    public override string Id => "weather";
    public override string Name => "Weather";
    public override string Category => "Essentials";
    public override string Description => "Current weather, the next hours and the week (Open-Meteo, no account).";
    public override double CardWidth => 340;

    WeatherIcon? _icon;
    TextBlock? _temp, _place;

    public WeatherWidget() { Weather.Instance.Changed += Update; }
    public override void Detach() => Weather.Instance.Changed -= Update;

    protected override FrameworkElement BuildTile()
    {
        _icon = new WeatherIcon { Width = Size * 0.62, Height = Size * 0.62 };
        _temp = TileText("", 15, FontWeights.SemiBold);
        _place = TileText("", 10, null, "Dock.Dim");
        _place.MaxWidth = 80 * Scale;
        Update();
        return Frame(Ui.Row(6 * Scale, _icon, Ui.Col(0, _temp, _place)), null, 10);
    }

    string Unit => Settings.Current.Fahrenheit ? "°F" : "°C";

    void Update()
    {
        var w = Weather.Instance.Current;
        if (_icon == null || _temp == null || _place == null) return;
        if (w == null)
        {
            _icon.Kind = Weather.Kind.PartSun;
            _temp.Text = Weather.Instance.HasPlace ? "—" : "Weather";
            _place.Text = Weather.Instance.HasPlace ? "Loading" : "Set a place";
        }
        else
        {
            _icon.Kind = Weather.KindOf(w.Code);
            _icon.Day = w.IsDay;
            _temp.Text = $"{w.Temp:0}°";
            _place.Text = Short(w.Place);
        }
        if (CardOpen) Host.RefreshCard(this);
    }

    static string Short(string place) => place.Split(',')[0];

    public override FrameworkElement? BuildCard()
    {
        var w = Weather.Instance.Current;
        if (!Weather.Instance.HasPlace || _choosing) return PlacePicker();
        if (w == null) return Ui.Col(10, Ui.Title("Weather"), Ui.Dim(Weather.Instance.Error ?? "Loading…"), Ui.Button("Change place", () => { _choosing = true; Host.RefreshCard(this); }));

        var big = new WeatherIcon { Kind = Weather.KindOf(w.Code), Day = w.IsDay, Width = 64, Height = 64 };
        var temp = Ui.Text($"{w.Temp:0}{Unit}", 34, FontWeights.SemiBold);
        var head = Ui.Line(
            Ui.Col(2, Ui.Title(Short(w.Place)), Ui.Dim(Weather.Describe(w.Code), 13), Ui.Dim($"Feels like {w.Feels:0}°", 11.5),
                Ui.Dim($"Humidity {w.Humidity:0}% · Wind {w.Wind:0} {(Settings.Current.Fahrenheit ? "mph" : "km/h")}", 11.5)),
            Ui.Row(4, big, temp));

        var hours = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
        foreach (var (time, t, code) in w.Hourly.Take(6))
        {
            var cell = Ui.Col(4, Center(Ui.Dim(time.ToString("HH:mm"), 11)), Center(new WeatherIcon { Kind = Weather.KindOf(code), Day = time.Hour is > 6 and < 20, Width = 24, Height = 24 }), Center(Ui.Text($"{t:0}°", 12.5, FontWeights.SemiBold)));
            hours.Children.Add(cell);
        }

        var days = new StackPanel();
        double lo = w.Daily.Count > 0 ? w.Daily.Min(d => d.Min) : 0, hi = w.Daily.Count > 0 ? w.Daily.Max(d => d.Max) : 1;
        foreach (var (day, min, max, code, rain) in w.Daily)
        {
            const double rangeW = 100;
            var name = Ui.Text(day.Date == DateTime.Today ? "Today" : day.ToString("ddd"), 12.5);
            name.Width = 46;
            var icon = new WeatherIcon { Kind = Weather.KindOf(code), Width = 22, Height = 22 };
            var rainT = Ui.Text(rain >= 20 ? $"{rain}%" : "", 11, null, "Accent");
            rainT.Width = 30;
            var range = new Grid { Height = 5, Width = rangeW, VerticalAlignment = VerticalAlignment.Center };
            var track = new Border { CornerRadius = new CornerRadius(2.5) };
            track.SetResourceReference(Border.BackgroundProperty, "Card.Track");
            double span = Math.Max(1, hi - lo);
            var fill = new Border { CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness((min - lo) / span * rangeW, 0, 0, 0), Width = Math.Max(6, (max - min) / span * rangeW) };
            fill.SetResourceReference(Border.BackgroundProperty, "Warn");
            range.Children.Add(track);
            range.Children.Add(fill);
            var minT = Ui.Dim($"{min:0}°", 12);
            minT.Width = 30;
            minT.TextAlignment = TextAlignment.Right;
            var maxT = Ui.Text($"{max:0}°", 12, FontWeights.SemiBold);
            days.Children.Add(Ui.Row(8, name, icon, rainT, minT, range, maxT));
        }

        string sun = w.Sunrise != default ? $"Sunrise {w.Sunrise:HH:mm} · Sunset {w.Sunset:HH:mm}" : "";
        var footer = Ui.Line(Ui.Dim(sun, 11.5), Ui.Button("Change place", () => { _choosing = true; Host.RefreshCard(this); }, "Flat"));
        return Ui.Col(12, head, Ui.Divider(), hours, Ui.Divider(), days, footer);
    }

    static FrameworkElement Center(FrameworkElement e) { e.HorizontalAlignment = HorizontalAlignment.Center; return e; }

    bool _choosing;

    FrameworkElement PlacePicker()
    {
        var results = new StackPanel();
        var field = Ui.Field(out var box, "", "Search for your town or city");
        var status = Ui.Dim("", 12);
        int searchId = 0;
        box.TextChanged += async (_, _) =>
        {
            int id = ++searchId;
            await Task.Delay(350);
            if (id != searchId) return;
            var q = box.Text;
            if (q.Trim().Length < 2) { results.Children.Clear(); return; }
            status.Text = "Searching…";
            var found = await Weather.SearchPlacesAsync(q);
            if (id != searchId) return;
            status.Text = found.Count == 0 ? "No places found." : "";
            results.Children.Clear();
            foreach (var (label, lat, lon) in found)
            {
                var t = Ui.Text(label, 13);
                t.MaxWidth = 290;
                results.Children.Add(Ui.ListRow(t, () =>
                {
                    _choosing = false;
                    Settings.Current.WeatherPlace = label;
                    Settings.Current.WeatherLat = lat;
                    Settings.Current.WeatherLon = lon;
                    Settings.SaveSoon();
                    _ = Weather.Instance.RefreshAsync();
                    Host.RefreshCard(this);
                }));
            }
        };
        box.Loaded += (_, _) => box.Focus();
        var units = Ui.Line(Ui.Text("Units", 13), Ui.Segmented(new[] { (false, "°C"), (true, "°F") }, Settings.Current.Fahrenheit,
            f => { Settings.Current.Fahrenheit = f; Settings.SaveSoon(); _ = Weather.Instance.RefreshAsync(); }));
        var col = Ui.Col(10, Ui.Title("Where are you?"), Ui.Wrap(Ui.Dim("Only the place you pick is sent to Open-Meteo to fetch the forecast.", 12)), field, status, results, units);
        if (Weather.Instance.HasPlace) col.Children.Add(Ui.Button("Cancel", () => { _choosing = false; Host.RefreshCard(this); }, "Flat"));
        return col;
    }

    public override void OnCardClosed() => _choosing = false;

    public override void AddMenuItems(ContextMenu menu)
    {
        menu.Items.Add(new MenuItem { Header = "Refresh", Tag = "" }.With(() => _ = Weather.Instance.RefreshAsync()));
    }
}
