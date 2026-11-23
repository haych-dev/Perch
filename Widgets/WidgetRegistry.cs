namespace Perch.Widgets;

public static class WidgetRegistry
{
    static readonly (string Id, Func<Widget> Make)[] All =
    [
        ("start", () => new StartWidget()),
        ("launcher", () => new LauncherWidget()),
        ("search", () => new SearchWidget()),
        ("tray", () => new TrayWidget()),
        ("wallpapers", () => new WallpaperWidget()),
        ("sep", () => new SeparatorWidget()),
        ("clock", () => new ClockWidget()),
        ("date", () => new DateWidget()),
        ("progress", () => new DayProgressWidget()),
        ("focus", () => new FocusWidget()),
        ("weather", () => new WeatherWidget()),
        ("cpu", () => new CpuWidget()),
        ("memory", () => new MemoryWidget()),
        ("network", () => new NetworkWidget()),
        ("storage", () => new StorageWidget()),
        ("battery", () => new BatteryWidget()),
        ("nowplaying", () => new NowPlayingWidget()),
        ("volume", () => new VolumeWidget()),
        ("toggles", () => new TogglesWidget()),
        ("todos", () => new TodoWidget()),
        ("notes", () => new NotesWidget()),
        ("desktop", () => new ShowDesktopWidget()),
        ("screenshot", () => new ScreenshotWidget()),
        ("downloads", () => new DownloadsWidget()),
        ("trash", () => new TrashWidget()),
    ];

    public static Widget? Create(string id) => All.FirstOrDefault(x => x.Id == id).Make?.Invoke();

    /// <summary>One instance of each widget, for the picker's descriptions.</summary>
    public static List<Widget> Catalogue() => All.Select(x => x.Make()).ToList();

    public static readonly string[] Categories = ["Essentials", "Time", "System", "Media", "Productivity", "Files"];
}
