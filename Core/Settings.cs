using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace Perch.Core;

public sealed class PinnedApp
{
    public string Name { get; set; } = "";
    /// <summary>What gets launched: a shortcut, an exe, or "shell:AppsFolder\{AppId}".</summary>
    public string Launch { get; set; } = "";
    /// <summary>Resolved executable, used to match running windows.</summary>
    public string? Target { get; set; }
    public string? AppId { get; set; }
}

public sealed class TodoItem
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public DateTime Added { get; set; } = DateTime.Now;
}

public sealed class Settings
{
    // Dock
    public string Edge { get; set; } = "bottom";            // bottom | top
    public string Style { get; set; } = "midnight";         // midnight | glass | black | white | clear
    public int IconSize { get; set; } = 44;
    public bool Magnify { get; set; } = true;
    public double MagnifyScale { get; set; } = 1.55;
    public bool FullWidth { get; set; }
    public int EdgeGap { get; set; } = 8;
    public int CornerRadius { get; set; } = 18;
    public int Spacing { get; set; } = 6;
    public int Padding { get; set; } = 7;
    public bool AutoHide { get; set; }
    public bool HideTaskbar { get; set; } = true;
    public bool ShowLabels { get; set; } = true;
    public string Indicator { get; set; } = "dot";          // dot | line
    public int Monitor { get; set; }                        // index into the monitor list; 0 is the primary display
    public bool Locked { get; set; }
    public bool HideInFullscreen { get; set; } = true;
    public string Accent { get; set; } = "#5AA9FF";

    // General
    public bool StartWithWindows { get; set; }
    public bool Clock24h { get; set; }
    public bool ClockSeconds { get; set; }
    public bool ClockDate { get; set; } = true;
    public string SearchEngine { get; set; } = "google";    // google | bing | duckduckgo

    // Weather (Open-Meteo, no key needed)
    public string WeatherPlace { get; set; } = "";
    public double WeatherLat { get; set; } = double.NaN;
    public double WeatherLon { get; set; } = double.NaN;
    public bool Fahrenheit { get; set; }

    public List<string> WorldClocks { get; set; } = [];

    /// <summary>Ordered dock contents: "start", "apps", "sep" and widget ids.</summary>
    public List<string> Items { get; set; } = ["start", "search", "apps", "sep", "nowplaying", "weather", "cpu", "memory", "volume", "toggles", "clock", "trash"];
    public List<PinnedApp> Pins { get; set; } = [];
    public bool PinsImported { get; set; }

    public List<TodoItem> Todos { get; set; } = [];
    public string Notes { get; set; } = "";
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;

    /// <summary>The taskbar's auto-hide state before Perch changed it; -1 when Perch has not touched it.</summary>
    public int TaskbarOriginalState { get; set; } = -1;

    // ─────────────────────────────────────────────

    public static Settings Current { get; private set; } = Load();

    /// <summary>Raised after a change. The flag says whether the dock needs to be rebuilt (size, edge, contents) or only refreshed.</summary>
    public static event Action<bool>? Changed;

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Perch");
    static string FilePath => Path.Combine(Folder, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static DispatcherTimer? _saveTimer;

    static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
                s.IconSize = Math.Clamp(s.IconSize, 24, 96);
                s.MagnifyScale = Math.Clamp(s.MagnifyScale, 1.0, 2.2);
                return s;
            }
        }
        catch { }
        return new Settings();
    }

    /// <summary>Applies a change, saves soon, and tells the dock.</summary>
    public static void Update(Action<Settings> change, bool rebuild = false)
    {
        change(Current);
        SaveSoon();
        Changed?.Invoke(rebuild);
    }

    public static void SaveSoon()
    {
        if (_saveTimer == null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public static void SaveNow()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
            File.Move(tmp, FilePath, true);
        }
        catch { }
    }
}
