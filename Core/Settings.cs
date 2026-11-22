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
    public List<string> Items { get; set; } = ["start", "search", "apps", "sep", "nowplaying", "weather", "cpu", "memory", "volume", "toggles", "tray", "clock", "trash"];

    /// <summary>Which one-time upgrades have been applied to these settings (see <see cref="Upgrade"/>).</summary>
    public int Schema { get; set; }
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
    // The weather location is NaN until a place is chosen; JSON has no NaN, so it is written as "NaN".
    // Without this, every save failed until a place was set.
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };
    static DispatcherTimer? _saveTimer;

    static Settings Load()
    {
        if (!File.Exists(FilePath)) return Fresh();
        // At sign-in the file can be briefly locked (antivirus, indexing), so a failed read is retried
        // rather than quietly starting with defaults, which would then be saved over the user's settings.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? Fresh();
                s.IconSize = Math.Clamp(s.IconSize, 24, 96);
                s.MagnifyScale = Math.Clamp(s.MagnifyScale, 1.0, 2.2);
                s.Upgrade();
                return s;
            }
            catch (IOException ex) when (attempt < 10)
            {
                Log.Error(ex, $"settings.json could not be read (attempt {attempt}); retrying");
                Thread.Sleep(300);
            }
            catch (Exception ex)
            {
                // Keep the unreadable file so nothing is lost, and say so in the log.
                Log.Error(ex, "settings.json could not be loaded; starting with defaults");
                try { File.Copy(FilePath, Path.Combine(Folder, $"settings.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true); } catch { }
                return Fresh();
            }
        }
    }

    const int CurrentSchema = 1;

    /// <summary>Settings for a first run: already up to date, so no upgrade is applied to them later.</summary>
    static Settings Fresh() => new() { Schema = CurrentSchema };

    /// <summary>One-time changes for settings saved by an older Perch.</summary>
    void Upgrade()
    {
        if (Schema < 1)
        {
            // 0.1.4 added the System tray button: put it on existing docks, just before the clock.
            if (!Items.Contains("tray"))
            {
                int clock = Items.IndexOf("clock");
                Items.Insert(clock >= 0 ? clock : Items.Count, "tray");
            }
            Schema = 1;
        }
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
        catch (Exception ex) { Log.Error(ex, "settings.json could not be saved"); }
    }
}

/// <summary>Appends errors to %AppData%\Perch\errors.log. Never throws.</summary>
public static class Log
{
    public static void Error(Exception ex, string? context = null)
    {
        try
        {
            Directory.CreateDirectory(Settings.Folder);
            File.AppendAllText(Path.Combine(Settings.Folder, "errors.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {(context != null ? context + ": " : "")}{ex}\n\n");
        }
        catch { }
    }
}
