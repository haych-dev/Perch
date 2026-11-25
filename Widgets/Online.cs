using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Perch.Core;
using Perch.Dock;

namespace Perch.Widgets;

/// <summary>Shared plumbing for online tiles: refresh on a timer while on the dock, remember the last error.</summary>
public abstract class OnlineWidget : Widget
{
    public override string Category => "Online";
    protected string? Error;
    protected bool Loading;
    readonly DispatcherTimer _timer = new();

    protected abstract TimeSpan Every { get; }
    protected abstract Task FetchAsync();
    protected abstract void Show();

    protected override FrameworkElement BuildTile()
    {
        var tile = BuildOnlineTile();
        _timer.Interval = Every;
        _timer.Tick += (_, _) => _ = Refresh();
        _timer.Start();
        _ = Refresh();
        return tile;
    }

    protected abstract FrameworkElement BuildOnlineTile();

    public override void Detach() => _timer.Stop();

    protected async Task Refresh()
    {
        if (Loading) return;
        Loading = true;
        try { await FetchAsync(); Error = null; }
        catch (Exception ex) { Error = Net.Describe(ex); if (ex is not HttpRequestException and not TaskCanceledException) Log.Error(ex, Name + " could not be read"); }
        finally { Loading = false; }
        Show();
        if (CardOpen) Host.RefreshCard(this);
    }

    /// <summary>A small "who is this" setup form for widgets that need a user name.</summary>
    protected FrameworkElement NameSetup(string title, string intro, string placeholder, string current, Action<string> save)
    {
        var field = Ui.Field(out var box, current, placeholder);
        box.Loaded += (_, _) => box.Focus();
        void Save() { if (box.Text.Trim().Length == 0) return; save(box.Text.Trim()); Settings.SaveSoon(); _ = Refresh(); Host.RefreshCard(this); }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); };
        return Ui.Col(10, Ui.Title(title), Ui.Wrap(Ui.Dim(intro, 12)), field, Ui.Button("Save", Save, "Primary"));
    }
}

// ───────────────────────── Currency ─────────────────────────

public sealed class CurrencyWidget : OnlineWidget
{
    public override string Id => "currency";
    public override string Name => "Currency";
    public override string Description => "Convert between 160 currencies with today's exchange rates.";
    public override double CardWidth => 320;
    protected override TimeSpan Every => TimeSpan.FromHours(1);

    static readonly Dictionary<string, (DateTime At, Dictionary<string, double> Rates)> Cache = new(StringComparer.OrdinalIgnoreCase);
    TextBlock? _value, _pair;

    protected override FrameworkElement BuildOnlineTile()
    {
        _value = TileText("…", 13, FontWeights.SemiBold);
        _pair = TileText("", 10, null, "Dock.Dim");
        return Frame(Ui.Row(7 * Scale, TileGlyph("", 16), Ui.Col(0, _value, _pair)), null, 10);
    }

    static async Task<Dictionary<string, double>> RatesFor(string from)
    {
        if (Cache.TryGetValue(from, out var c) && DateTime.Now - c.At < TimeSpan.FromHours(1)) return c.Rates;
        using var doc = JsonDocument.Parse(await Net.Http.GetStringAsync("https://open.er-api.com/v6/latest/" + Uri.EscapeDataString(from)));
        if (doc.RootElement.GetProperty("result").GetString() != "success") throw new InvalidOperationException("Unknown currency " + from);
        var rates = doc.RootElement.GetProperty("rates").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble(), StringComparer.OrdinalIgnoreCase);
        Cache[from] = (DateTime.Now, rates);
        return rates;
    }

    Dictionary<string, double>? _rates;
    protected override async Task FetchAsync() => _rates = await RatesFor(Settings.Current.CurrencyFrom);

    protected override void Show()
    {
        var s = Settings.Current;
        if (_value == null || _pair == null) return;
        _pair.Text = $"1 {s.CurrencyFrom} in {s.CurrencyTo}";
        _value.Text = _rates != null && _rates.TryGetValue(s.CurrencyTo, out var r) ? Money(r) : Error != null ? "—" : "…";
    }

    static string Money(double v) => v >= 100 ? v.ToString("N0", CultureInfo.CurrentCulture) : v >= 1 ? v.ToString("N2", CultureInfo.CurrentCulture) : v.ToString("0.####", CultureInfo.CurrentCulture);

    public override FrameworkElement? BuildCard()
    {
        var s = Settings.Current;
        var amountField = Ui.Field(out var amount, s.CurrencyAmount.ToString(CultureInfo.CurrentCulture), "Amount");
        var fromField = Ui.Field(out var from, s.CurrencyFrom, "From");
        var toField = Ui.Field(out var to, s.CurrencyTo, "To");
        fromField.Width = toField.Width = 80;
        amountField.Width = 110;
        var result = Ui.Text("", 24, FontWeights.SemiBold);
        var note = Ui.Dim("", 11.5);
        var others = new StackPanel();

        async void Recalc()
        {
            if (!double.TryParse(amount.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var a)) { result.Text = "—"; return; }
            string f = from.Text.Trim().ToUpperInvariant(), t = to.Text.Trim().ToUpperInvariant();
            if (f.Length != 3 || t.Length != 3) { result.Text = "—"; note.Text = "Use 3-letter codes, like AUD or EUR."; return; }
            try
            {
                var rates = await RatesFor(f);
                if (!rates.TryGetValue(t, out var r)) { result.Text = "—"; note.Text = $"No rate for {t}."; return; }
                result.Text = $"{Money(a * r)} {t}";
                note.Text = $"1 {f} = {Money(r)} {t}";
                others.Children.Clear();
                foreach (var code in new[] { "USD", "EUR", "GBP", "JPY", "NZD", "CAD", "CNY" }.Where(c => c != f && c != t).Take(5))
                    if (rates.TryGetValue(code, out var rr)) others.Children.Add(Ui.Line(Ui.Dim(code, 12.5), Ui.Text(Money(a * rr), 12.5, FontWeights.SemiBold)));
                s.CurrencyAmount = a; s.CurrencyFrom = f; s.CurrencyTo = t; Settings.SaveSoon();
                _rates = rates;
                Show();
            }
            catch (Exception ex) { result.Text = "—"; note.Text = ex is InvalidOperationException ? $"Unknown currency {f}." : Net.Describe(ex); }
        }
        amount.TextChanged += (_, _) => Recalc();
        from.LostFocus += (_, _) => Recalc();
        to.LostFocus += (_, _) => Recalc();
        from.KeyDown += (_, e) => { if (e.Key == Key.Enter) Recalc(); };
        to.KeyDown += (_, e) => { if (e.Key == Key.Enter) Recalc(); };
        var swap = Ui.IconButton("", () => { (from.Text, to.Text) = (to.Text, from.Text); Recalc(); }, 13, "Swap");
        Recalc();
        var credit = Ui.Dim("Rates by exchangerate-api.com, updated daily", 10.5);
        return Ui.Col(10, Ui.Title("Currency"), Ui.Row(6, amountField, fromField, swap, toField), result, note, Ui.Divider(), others, credit);
    }
}

// ───────────────────────── Crypto ─────────────────────────

public sealed class CryptoWidget : OnlineWidget
{
    public override string Id => "crypto";
    public override string Name => "Crypto";
    public override string Description => "Live prices and 24-hour change for the coins you choose (Binance public prices).";
    public override double CardWidth => 330;
    protected override TimeSpan Every => TimeSpan.FromMinutes(1);

    sealed record Quote(string Symbol, double Price, double ChangePct);
    List<Quote> _quotes = [];
    readonly Dictionary<string, Ring> _history = [];
    TextBlock? _price, _change;

    protected override FrameworkElement BuildOnlineTile()
    {
        _price = TileText("…", 13, FontWeights.SemiBold);
        _change = TileText("", 10, null, "Dock.Dim");
        return Frame(Ui.Row(7 * Scale, TileGlyph("", 15), Ui.Col(0, _price, _change)), null, 10);
    }

    static string Pair(string coin) => coin.ToUpperInvariant() + "USDT";

    protected override async Task FetchAsync()
    {
        var coins = Settings.Current.CryptoSymbols.Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
        if (coins.Count == 0) { _quotes = []; return; }
        var list = new List<Quote>();
        foreach (var coin in coins)
        {
            try
            {
                using var doc = JsonDocument.Parse(await Net.Http.GetStringAsync("https://api.binance.com/api/v3/ticker/24hr?symbol=" + Pair(coin)));
                var r = doc.RootElement;
                list.Add(new Quote(coin.ToUpperInvariant(), double.Parse(r.GetProperty("lastPrice").GetString()!, CultureInfo.InvariantCulture), double.Parse(r.GetProperty("priceChangePercent").GetString()!, CultureInfo.InvariantCulture)));
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest) { list.Add(new Quote(coin.ToUpperInvariant(), double.NaN, 0)); }
        }
        _quotes = list;
    }

    async Task FetchHistory()
    {
        foreach (var q in _quotes.Where(q => !double.IsNaN(q.Price)))
        {
            try
            {
                using var doc = JsonDocument.Parse(await Net.Http.GetStringAsync($"https://api.binance.com/api/v3/klines?symbol={Pair(q.Symbol)}&interval=1h&limit=24"));
                var ring = new Ring(24);
                foreach (var k in doc.RootElement.EnumerateArray()) ring.Push(double.Parse(k[4].GetString()!, CultureInfo.InvariantCulture));
                _history[q.Symbol] = ring;
            }
            catch { }
        }
    }

    static string Usd(double v) => "$" + (v >= 1000 ? v.ToString("N0", CultureInfo.CurrentCulture) : v >= 1 ? v.ToString("N2", CultureInfo.CurrentCulture) : v.ToString("0.#####", CultureInfo.CurrentCulture));

    protected override void Show()
    {
        if (_price == null || _change == null) return;
        var q = _quotes.FirstOrDefault(x => !double.IsNaN(x.Price));
        if (q == null) { _price.Text = Error != null ? "—" : "Crypto"; _change.Text = Error ?? "Pick coins"; return; }
        _price.Text = $"{q.Symbol} {Usd(q.Price)}";
        _change.Text = $"{(q.ChangePct >= 0 ? "▲" : "▼")} {Math.Abs(q.ChangePct):0.0}% today";
        _change.SetResourceReference(TextBlock.ForegroundProperty, q.ChangePct >= 0 ? "Good" : "Bad");
    }

    bool _editing, _historyTried;
    public override void OnCardClosed() { _editing = false; _historyTried = false; }

    /// <summary>Charts are fetched once per opening of the card, then the card is redrawn.</summary>
    async Task LoadHistory()
    {
        _historyTried = true;
        await FetchHistory();
        if (CardOpen) Host.RefreshCard(this);
    }

    public override FrameworkElement? BuildCard()
    {
        if (_editing)
            return NameSetup("Coins to show", "Enter coin symbols separated by commas, for example BTC, ETH, SOL. Prices are in US dollars.", "BTC, ETH, SOL",
                string.Join(", ", Settings.Current.CryptoSymbols), v => { Settings.Current.CryptoSymbols = v.Split(',', ' ').Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).ToList(); _editing = false; _history.Clear(); });

        if (!_historyTried && _quotes.Count > 0) _ = LoadHistory();
        var list = new StackPanel();
        foreach (var q in _quotes)
        {
            if (double.IsNaN(q.Price)) { list.Children.Add(Ui.Line(Ui.Text(q.Symbol, 13, FontWeights.SemiBold), Ui.Dim("not on Binance", 12))); continue; }
            var spark = new Sparkline { Width = 90, Height = 26, AutoScale = false, BrushKey = q.ChangePct >= 0 ? "Good" : "Bad" };
            if (_history.TryGetValue(q.Symbol, out var h)) { spark.Data = Normalise(h); spark.Max = 100; }
            var change = Ui.Text($"{(q.ChangePct >= 0 ? "+" : "")}{q.ChangePct:0.00}%", 12, FontWeights.SemiBold, q.ChangePct >= 0 ? "Good" : "Bad");
            change.Width = 62;
            change.TextAlignment = TextAlignment.Right;
            var left = Ui.Col(0, Ui.Text(q.Symbol, 13, FontWeights.SemiBold), Ui.Dim(Usd(q.Price), 12));
            ((FrameworkElement)left).Width = 110;
            list.Children.Add(new Border { Padding = new Thickness(0, 4, 0, 4), Child = Ui.Row(8, left, spark, change) });
        }
        if (_quotes.Count == 0) list.Children.Add(Ui.Dim(Error ?? "Loading…", 12.5));
        var edit = Ui.Button("Change coins", () => { _editing = true; Host.RefreshCard(this); });
        return Ui.Col(10, Ui.Line(Ui.Title("Crypto"), edit), list, Ui.Dim("Last 24 hours · prices from Binance, in US dollars", 10.5));
    }

    /// <summary>Scales a price history to 0–100 so the sparkline uses its full height.</summary>
    static Ring Normalise(Ring h)
    {
        double min = double.MaxValue, max = double.MinValue;
        for (int i = 0; i < h.Count; i++) { min = Math.Min(min, h[i]); max = Math.Max(max, h[i]); }
        var r = new Ring(h.Capacity);
        for (int i = 0; i < h.Count; i++) r.Push(max > min ? 8 + 84 * (h[i] - min) / (max - min) : 50);
        return r;
    }
}

// ───────────────────────── GitHub ─────────────────────────

public sealed class GitHubWidget : OnlineWidget
{
    public override string Id => "github";
    public override string Name => "GitHub activity";
    public override string Description => "Your contribution graph and latest public activity, for any GitHub user name.";
    public override double CardWidth => 420;
    protected override TimeSpan Every => TimeSpan.FromMinutes(30);

    List<(DateTime Day, int Level)> _days = [];
    string _total = "";
    List<string> _events = [];
    Heatmap? _mini;
    TextBlock? _label;
    bool _setup;

    protected override FrameworkElement BuildOnlineTile()
    {
        _mini = new Heatmap { Weeks = 12, Cell = 4.2 * Scale, Gap = 1.3 * Scale };
        _label = TileText("GitHub", 10, null, "Dock.Dim");
        return Frame(Ui.Col(3 * Scale, _mini, _label), null, 10);
    }

    protected override async Task FetchAsync()
    {
        var user = Settings.Current.GitHubUser;
        if (user.Length == 0) { _days = []; return; }
        string html = await Net.Http.GetStringAsync($"https://github.com/users/{Uri.EscapeDataString(user)}/contributions");
        // Cells are listed by weekday, not date, so collect then sort.
        _days = Regex.Matches(html, "data-date=\"(\\d{4}-\\d{2}-\\d{2})\"[^>]*?data-level=\"(\\d)\"")
            .Select(m => (DateTime.ParseExact(m.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value)))
            .OrderBy(d => d.Item1).ToList();
        var t = Regex.Match(html, "([\\d,]+)\\s+contributions?\\s+in the last year");
        _total = t.Success ? t.Groups[1].Value : "";
        try
        {
            using var doc = JsonDocument.Parse(await Net.Http.GetStringAsync($"https://api.github.com/users/{Uri.EscapeDataString(user)}/events/public?per_page=8"));
            _events = doc.RootElement.EnumerateArray().Select(Describe).Where(x => x.Length > 0).Take(6).ToList();
        }
        catch { _events = []; }
    }

    static string Describe(JsonElement e)
    {
        string repo = e.GetProperty("repo").GetProperty("name").GetString() ?? "";
        var when = e.TryGetProperty("created_at", out var c) ? DownloadsWidget.Ago(c.GetDateTime().ToLocalTime()) : "";
        string what = e.GetProperty("type").GetString() switch
        {
            "PushEvent" => "Pushed to",
            "PullRequestEvent" => "Pull request in",
            "IssuesEvent" => "Issue in",
            "IssueCommentEvent" => "Commented in",
            "CreateEvent" => "Created",
            "ReleaseEvent" => "Released",
            "WatchEvent" => "Starred",
            "ForkEvent" => "Forked",
            "PullRequestReviewEvent" => "Reviewed in",
            _ => "",
        };
        return what.Length == 0 ? "" : $"{what} {repo} · {when}";
    }

    protected override void Show()
    {
        if (_mini == null || _label == null) return;
        _mini.Days = _days;
        _mini.InvalidateVisual();
        var user = Settings.Current.GitHubUser;
        _label.Text = user.Length == 0 ? "Set a user name" : Error != null && _days.Count == 0 ? Error : _days.Count > 0 ? $"{_total} this year" : "Loading…";
    }

    public override void OnCardClosed() => _setup = false;

    public override FrameworkElement? BuildCard()
    {
        var s = Settings.Current;
        if (_setup || s.GitHubUser.Length == 0)
            return NameSetup("GitHub activity", "Enter a GitHub user name. Only public activity is shown, and nothing is signed in.", "User name", s.GitHubUser,
                v => { s.GitHubUser = v; _setup = false; _days = []; });
        var big = new Heatmap { Weeks = 53, Cell = 5.5, Gap = 1.6, InCard = true, Days = _days };
        var head = Ui.Line(Ui.Col(0, Ui.Title(s.GitHubUser), Ui.Dim(_total.Length > 0 ? $"{_total} contributions in the last year" : Error ?? "Loading…", 12)),
            Ui.Row(4, Ui.Button("Profile", () => { Apps.Open("https://github.com/" + s.GitHubUser); Host.CloseCard(); }), Ui.IconButton("", () => { _setup = true; Host.RefreshCard(this); }, 12, "Change user")));
        var events = new StackPanel();
        foreach (var e in _events) { var t = Ui.Text(e, 12); t.MaxWidth = 380; events.Children.Add(new Border { Padding = new Thickness(0, 3, 0, 3), Child = t }); }
        if (_events.Count == 0) events.Children.Add(Ui.Dim("No recent public activity.", 12));
        return Ui.Col(10, head, big, Ui.Divider(), Ui.Dim("Recent", 11.5), events);
    }

    sealed class Heatmap : FrameworkElement
    {
        public int Weeks = 12;
        public double Cell = 4, Gap = 1.3;
        public bool InCard;
        public List<(DateTime Day, int Level)> Days = [];

        protected override Size MeasureOverride(Size available) => new(Weeks * (Cell + Gap) - Gap, 7 * (Cell + Gap) - Gap);

        protected override void OnRender(DrawingContext dc)
        {
            var g = Theme.ColorOf("Good");
            var empty = Theme.Brush(InCard ? "Card.Track" : "Tile.Hover");
            var brushes = new Brush[5];
            brushes[0] = empty;
            for (int l = 1; l <= 4; l++) { var b = new SolidColorBrush(Color.FromArgb((byte)(70 + l * 46), g.R, g.G, g.B)); b.Freeze(); brushes[l] = b; }
            var levels = Days.ToDictionary(d => d.Day.Date, d => d.Level);
            // Columns are weeks ending this week; rows are Sunday to Saturday, as on GitHub.
            var today = DateTime.Today;
            var lastSunday = today.AddDays(-(int)today.DayOfWeek);
            for (int w = 0; w < Weeks; w++)
            {
                var weekStart = lastSunday.AddDays(-7 * (Weeks - 1 - w));
                for (int d = 0; d < 7; d++)
                {
                    var day = weekStart.AddDays(d);
                    if (day > today) continue;
                    int lv = levels.TryGetValue(day, out var v) ? v : 0;
                    dc.DrawRoundedRectangle(brushes[Math.Clamp(lv, 0, 4)], null, new Rect(w * (Cell + Gap), d * (Cell + Gap), Cell, Cell), Cell * 0.25, Cell * 0.25);
                }
            }
        }
    }
}

// ───────────────────────── LeetCode ─────────────────────────

public sealed class LeetCodeWidget : OnlineWidget
{
    public override string Id => "leetcode";
    public override string Name => "LeetCode";
    public override string Description => "Problems solved on LeetCode, by difficulty, for any user name.";
    public override double CardWidth => 320;
    protected override TimeSpan Every => TimeSpan.FromHours(1);

    readonly Dictionary<string, (int Solved, int Total)> _stats = [];
    RingGauge? _ring;
    TextBlock? _count, _label;
    bool _setup;

    protected override FrameworkElement BuildOnlineTile()
    {
        _ring = new RingGauge { Width = Size * 0.56, Height = Size * 0.56, Thickness = 3.5 * Scale, BrushKey = "Warn" };
        _count = TileText("…", 13, FontWeights.SemiBold);
        _label = TileText("LeetCode", 10, null, "Dock.Dim");
        return Frame(Ui.Row(8 * Scale, _ring, Ui.Col(0, _count, _label)), null, 10);
    }

    protected override async Task FetchAsync()
    {
        var user = Settings.Current.LeetCodeUser;
        _stats.Clear();
        if (user.Length == 0) return;
        var body = JsonSerializer.Serialize(new
        {
            query = "query($u:String!){matchedUser(username:$u){submitStatsGlobal{acSubmissionNum{difficulty count}}} allQuestionsCount{difficulty count}}",
            variables = new { u = user },
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://leetcode.com/graphql") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.Referrer = new Uri("https://leetcode.com");
        using var resp = await Net.Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        if (data.GetProperty("matchedUser").ValueKind == JsonValueKind.Null) throw new HttpRequestException("not found", null, System.Net.HttpStatusCode.NotFound);
        var totals = data.GetProperty("allQuestionsCount").EnumerateArray().ToDictionary(x => x.GetProperty("difficulty").GetString()!, x => x.GetProperty("count").GetInt32());
        foreach (var x in data.GetProperty("matchedUser").GetProperty("submitStatsGlobal").GetProperty("acSubmissionNum").EnumerateArray())
        {
            var d = x.GetProperty("difficulty").GetString()!;
            _stats[d] = (x.GetProperty("count").GetInt32(), totals.GetValueOrDefault(d));
        }
    }

    protected override void Show()
    {
        if (_ring == null || _count == null || _label == null) return;
        if (Settings.Current.LeetCodeUser.Length == 0) { _count.Text = "LeetCode"; _label.Text = "Set a user name"; _ring.Value = 0; return; }
        if (!_stats.TryGetValue("All", out var all)) { _count.Text = "—"; _label.Text = Error ?? "Loading…"; return; }
        _count.Text = $"{all.Solved} solved";
        _label.Text = $"of {all.Total:N0}";
        _ring.Value = all.Total > 0 ? 100.0 * all.Solved / all.Total : 0;
    }

    public override void OnCardClosed() => _setup = false;

    public override FrameworkElement? BuildCard()
    {
        var s = Settings.Current;
        if (_setup || s.LeetCodeUser.Length == 0)
            return NameSetup("LeetCode", "Enter a LeetCode user name. Only public profile numbers are read.", "User name", s.LeetCodeUser,
                v => { s.LeetCodeUser = v; _setup = false; });
        var col = Ui.Col(12, Ui.Line(Ui.Title(s.LeetCodeUser), Ui.Row(4, Ui.Button("Profile", () => { Apps.Open("https://leetcode.com/u/" + s.LeetCodeUser); Host.CloseCard(); }), Ui.IconButton("", () => { _setup = true; Host.RefreshCard(this); }, 12, "Change user"))));
        if (_stats.Count == 0) { col.Children.Add(Ui.Dim(Error ?? "Loading…", 12.5)); return col; }
        foreach (var (d, brush) in new[] { ("Easy", "Good"), ("Medium", "Warn"), ("Hard", "Bad") })
        {
            if (!_stats.TryGetValue(d, out var v)) continue;
            col.Children.Add(Ui.Col(5, Ui.Line(Ui.Text(d, 13), Ui.Text($"{v.Solved} / {v.Total}", 13, FontWeights.SemiBold)), Ui.Bar(v.Total > 0 ? 100.0 * v.Solved / v.Total : 0, brush)));
        }
        return col;
    }
}
