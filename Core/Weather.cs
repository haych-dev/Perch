using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;

namespace Perch.Core;

public sealed class WeatherNow
{
    public string Place = "";
    public double Temp, Feels, Wind, Humidity;
    public int Code;
    public bool IsDay = true;
    public List<(DateTime Time, double Temp, int Code)> Hourly = [];
    public List<(DateTime Day, double Min, double Max, int Code, int RainPct)> Daily = [];
    public DateTime Sunrise, Sunset;
    public DateTime Fetched;
}

/// <summary>Forecasts from Open-Meteo (free, no account or key). Only the chosen place is sent.</summary>
public sealed class Weather
{
    public static Weather Instance { get; } = new();
    public event Action? Changed;
    public WeatherNow? Current { get; private set; }
    public string? Error { get; private set; }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    DispatcherTimer? _timer;

    static Weather() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("Perch/0.1");

    public void Start()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    public bool HasPlace => !double.IsNaN(Settings.Current.WeatherLat) && !double.IsNaN(Settings.Current.WeatherLon);

    public async Task RefreshAsync()
    {
        var s = Settings.Current;
        if (!HasPlace) { Current = null; Error = null; Changed?.Invoke(); return; }
        try
        {
            string unit = s.Fahrenheit ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : "";
            string lat = s.WeatherLat.ToString("0.####", CultureInfo.InvariantCulture), lon = s.WeatherLon.ToString("0.####", CultureInfo.InvariantCulture);
            string url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
                "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,is_day" +
                "&hourly=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,precipitation_probability_max" +
                $"&timezone=auto&forecast_days=7{unit}";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var root = doc.RootElement;
            var cur = root.GetProperty("current");
            var w = new WeatherNow
            {
                Place = s.WeatherPlace,
                Temp = cur.GetProperty("temperature_2m").GetDouble(),
                Feels = cur.GetProperty("apparent_temperature").GetDouble(),
                Humidity = cur.GetProperty("relative_humidity_2m").GetDouble(),
                Wind = cur.GetProperty("wind_speed_10m").GetDouble(),
                Code = cur.GetProperty("weather_code").GetInt32(),
                IsDay = cur.GetProperty("is_day").GetInt32() == 1,
                Fetched = DateTime.Now,
            };
            var hourly = root.GetProperty("hourly");
            var ht = hourly.GetProperty("time").EnumerateArray().Select(e => DateTime.Parse(e.GetString()!, CultureInfo.InvariantCulture)).ToList();
            var htemp = hourly.GetProperty("temperature_2m").EnumerateArray().Select(e => e.GetDouble()).ToList();
            var hcode = hourly.GetProperty("weather_code").EnumerateArray().Select(e => e.GetInt32()).ToList();
            DateTime localNow = DateTime.Parse(cur.GetProperty("time").GetString()!, CultureInfo.InvariantCulture);
            for (int i = 0; i < ht.Count && w.Hourly.Count < 12; i++)
                if (ht[i] >= localNow.AddMinutes(-30)) w.Hourly.Add((ht[i], htemp[i], hcode[i]));

            var daily = root.GetProperty("daily");
            var dt = daily.GetProperty("time").EnumerateArray().Select(e => DateTime.Parse(e.GetString()!, CultureInfo.InvariantCulture)).ToList();
            var dmax = daily.GetProperty("temperature_2m_max").EnumerateArray().Select(e => e.GetDouble()).ToList();
            var dmin = daily.GetProperty("temperature_2m_min").EnumerateArray().Select(e => e.GetDouble()).ToList();
            var dcode = daily.GetProperty("weather_code").EnumerateArray().Select(e => e.GetInt32()).ToList();
            var drain = daily.GetProperty("precipitation_probability_max").EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 0).ToList();
            for (int i = 0; i < dt.Count; i++) w.Daily.Add((dt[i], dmin[i], dmax[i], dcode[i], drain[i]));
            var sr = daily.GetProperty("sunrise").EnumerateArray().FirstOrDefault().GetString();
            var ss = daily.GetProperty("sunset").EnumerateArray().FirstOrDefault().GetString();
            if (sr != null) w.Sunrise = DateTime.Parse(sr, CultureInfo.InvariantCulture);
            if (ss != null) w.Sunset = DateTime.Parse(ss, CultureInfo.InvariantCulture);

            Current = w;
            Error = null;
        }
        catch (Exception ex) { Error = ex is HttpRequestException or TaskCanceledException ? "Can't reach the weather service" : "Weather unavailable"; }
        Changed?.Invoke();
    }

    public static async Task<List<(string Label, double Lat, double Lon)>> SearchPlacesAsync(string query)
    {
        var list = new List<(string, double, double)>();
        if (string.IsNullOrWhiteSpace(query)) return list;
        try
        {
            string url = "https://geocoding-api.open-meteo.com/v1/search?count=6&language=en&format=json&name=" + Uri.EscapeDataString(query.Trim());
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            if (!doc.RootElement.TryGetProperty("results", out var results)) return list;
            foreach (var r in results.EnumerateArray())
            {
                string name = r.GetProperty("name").GetString() ?? "";
                string admin = r.TryGetProperty("admin1", out var a) ? a.GetString() ?? "" : "";
                string country = r.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "";
                string label = string.Join(", ", new[] { name, admin, country }.Where(x => x.Length > 0).Distinct());
                list.Add((label, r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble()));
            }
        }
        catch { }
        return list;
    }

    public static string Describe(int code) => code switch
    {
        0 => "Clear",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 => "Light rain",
        63 => "Rain",
        65 => "Heavy rain",
        66 or 67 => "Freezing rain",
        71 => "Light snow",
        73 => "Snow",
        75 => "Heavy snow",
        77 => "Snow grains",
        80 or 81 => "Showers",
        82 => "Heavy showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm, hail",
        _ => "—",
    };

    public enum Kind { Sun, PartSun, Cloud, Fog, Rain, Snow, Storm }

    public static Kind KindOf(int code) => code switch
    {
        0 or 1 => Kind.Sun,
        2 => Kind.PartSun,
        3 => Kind.Cloud,
        45 or 48 => Kind.Fog,
        >= 51 and <= 67 or >= 80 and <= 82 => Kind.Rain,
        >= 71 and <= 77 or 85 or 86 => Kind.Snow,
        >= 95 => Kind.Storm,
        _ => Kind.Cloud,
    };
}
