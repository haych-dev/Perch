using System.Diagnostics;
using System.IO;

namespace Perch.Core;

/// <summary>Pinned apps: importing the taskbar's pins, launching, matching running windows.</summary>
public static class Apps
{
    static string TaskbarPins => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    /// <summary>On first run, copies the Windows taskbar's pinned shortcuts so the dock starts out familiar.</summary>
    public static void ImportTaskbarPins()
    {
        var s = Settings.Current;
        if (s.PinsImported) return;
        s.PinsImported = true;
        try
        {
            if (!Directory.Exists(TaskbarPins)) return;
            foreach (var lnk in Directory.GetFiles(TaskbarPins, "*.lnk").OrderBy(File.GetCreationTime))
            {
                var pin = FromShortcut(lnk);
                if (pin != null && !s.Pins.Any(p => Same(p, pin))) s.Pins.Add(pin);
            }
        }
        catch { }
        Settings.SaveSoon();
    }

    public static PinnedApp? FromShortcut(string lnk)
    {
        var (target, appId, _) = Native.ReadShortcut(lnk);
        // "File Explorer" is a shortcut to explorer.exe; its windows report explorer.exe too.
        return new PinnedApp { Name = Path.GetFileNameWithoutExtension(lnk), Launch = lnk, Target = target, AppId = appId };
    }

    public static PinnedApp FromGroup(AppGroup g)
    {
        bool packaged = g.ExePath == null || g.ExePath.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(g.ExePath).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
        string launch = packaged && g.AppId != null ? "shell:AppsFolder\\" + g.AppId : g.ExePath ?? "";
        return new PinnedApp { Name = g.Name, Launch = launch, Target = packaged ? null : g.ExePath, AppId = g.AppId };
    }

    public static PinnedApp? FromFile(string path)
    {
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return FromShortcut(path);
        if (File.Exists(path) || Directory.Exists(path))
            return new PinnedApp { Name = Path.GetFileNameWithoutExtension(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path, Launch = path, Target = File.Exists(path) ? path : null };
        return null;
    }

    static bool Same(PinnedApp a, PinnedApp b) =>
        (a.AppId != null && string.Equals(a.AppId, b.AppId, StringComparison.OrdinalIgnoreCase))
        || (a.Target != null && string.Equals(a.Target, b.Target, StringComparison.OrdinalIgnoreCase))
        || string.Equals(a.Launch, b.Launch, StringComparison.OrdinalIgnoreCase);

    public static bool Matches(PinnedApp pin, AppGroup g)
    {
        if (pin.AppId != null && g.AppId != null) return string.Equals(pin.AppId, g.AppId, StringComparison.OrdinalIgnoreCase);
        if (pin.Target != null && g.ExePath != null && string.Equals(pin.Target, g.ExePath, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static bool IsPinned(AppGroup g) => Settings.Current.Pins.Any(p => Matches(p, g));

    public static void Pin(PinnedApp pin)
    {
        if (Settings.Current.Pins.Any(p => Same(p, pin))) return;
        Settings.Update(s => s.Pins.Add(pin), rebuild: false);
    }

    public static void Unpin(PinnedApp pin) => Settings.Update(s => s.Pins.Remove(pin));

    public static string? IconSource(PinnedApp pin)
    {
        if (pin.Launch.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return pin.Launch;
        return pin.Launch;
    }

    public static void Launch(PinnedApp pin, string? args = null) => Launch(pin.Launch, args);

    public static void Launch(string target, string? args = null)
    {
        try
        {
            if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Arguments = args ?? "", WorkingDirectory = SafeDir(target) });
        }
        catch (Exception ex) { Debug.WriteLine("Launch failed: " + ex.Message); }
    }

    static string SafeDir(string target)
    {
        try { return Path.GetDirectoryName(target) ?? ""; } catch { return ""; }
    }

    public static void OpenFileLocation(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return;
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
    }

    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }

    // ───────────── Start menu apps (for search) ─────────────
    static List<(string Name, string Path)>? _startApps;
    static DateTime _startAppsAt;

    public static List<(string Name, string Path)> StartMenuApps()
    {
        if (_startApps != null && DateTime.Now - _startAppsAt < TimeSpan.FromMinutes(5)) return _startApps;
        var list = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "Programs"), "*.*", SearchOption.AllDirectories))
                {
                    if (!f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) continue;
                    var name = Path.GetFileNameWithoutExtension(f);
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                    if (seen.Add(name)) list.Add((name, f));
                }
            }
            catch { }
        }
        _startApps = list.OrderBy(x => x.Item1).ToList();
        _startAppsAt = DateTime.Now;
        return _startApps;
    }

    // ───────────── Start menu apps (what Start's app list shows) ─────────────

    /// <summary>One app as Start lists it. Id is a shortcut path or a Store app's app user model ID.</summary>
    public sealed record AppEntry(string Name, string Id, string? AppId, string? Target)
    {
        public bool IsShortcut => Id.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
        /// <summary>What to ask the shell for an icon.</summary>
        public string IconSource => IsShortcut ? Id : "shell:AppsFolder\\" + Id;
    }

    static List<AppEntry>? _allApps;
    static DateTime _allAppsAt;

    // Folders Start keeps out of its app list (Windows' admin consoles live in Windows Tools instead).
    static readonly string[] HiddenStartFolders = ["Administrative Tools", "Windows Tools", "System Tools", "Windows Administrative Tools", "Startup"];

    // Shortcuts that sit next to apps in Start folders but aren't apps.
    static readonly System.Text.RegularExpressions.Regex NotAnApp = new(
        @"(^uninstall|\buninstall$|\buninstall\s|\bhelp\b|help&support|manuals?\b|documentation|\bfaqs?\b|release notes|revision history|example scripts|module docs|website|on the web|support center|readme|read me|license|\bchanges\b|diagnostics?\b|logs collector|getting started|what's new|reset preferences|^administrative tools$)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// The apps Start shows: shortcuts in the Start menu folders that open a program, plus Store apps. Documents,
    /// web links, help files, uninstallers and Windows' admin tools are left out. Call on the UI thread.
    /// </summary>
    public static List<AppEntry> AllApps()
    {
        if (_allApps != null && DateTime.Now - _allAppsAt < TimeSpan.FromMinutes(5)) return _allApps;
        var list = new List<AppEntry>();

        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
        {
            string programs = Path.Combine(root, "Programs");
            if (!Directory.Exists(programs)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(programs, "*.lnk", SearchOption.AllDirectories).ToList(); } catch { continue; }
            foreach (var lnk in files)
            {
                var rel = Path.GetRelativePath(programs, lnk);
                if (HiddenStartFolders.Any(f => rel.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase))) continue;
                var name = Path.GetFileNameWithoutExtension(lnk);
                if (NotAnApp.IsMatch(name)) continue;
                var (target, appId, _) = Native.ReadShortcut(lnk);
                // Keep shortcuts to programs, and app shortcuts with no file target (they launch by app ID).
                if (!string.IsNullOrEmpty(target))
                {
                    var ext = Path.GetExtension(target).ToLowerInvariant();
                    if (ext != ".exe") continue;
                    var exeName = Path.GetFileNameWithoutExtension(target);
                    if (exeName.StartsWith("unins", StringComparison.OrdinalIgnoreCase) || exeName.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                }
                list.Add(new AppEntry(name, lnk, appId, string.IsNullOrEmpty(target) ? null : target));
            }
        }

        // Store apps (Notepad, Calculator, Photos…) aren't shortcuts; the shell's Applications folder lists them.
        try
        {
            var t = Type.GetTypeFromProgID("Shell.Application");
            if (t != null)
            {
                dynamic shell = Activator.CreateInstance(t)!;
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                foreach (dynamic item in folder.Items())
                {
                    string name = item.Name, id = item.Path;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id) || !id.Contains('!') || NotAnApp.IsMatch(name)) continue;
                    list.Add(new AppEntry(name, id, id, null));
                }
            }
        }
        catch (Exception ex) { Log.Error(ex, "Store apps could not be listed"); }

        // One entry per name; a shortcut wins over a Store entry of the same name.
        _allApps = list.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(a => a.IsShortcut).First())
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        _allAppsAt = DateTime.Now;
        return _allApps;
    }

    /// <summary>A pin for an entry from <see cref="AllApps"/>.</summary>
    public static PinnedApp FromAllApps(AppEntry e) =>
        e.IsShortcut
            ? new PinnedApp { Name = e.Name, Launch = e.Id, Target = e.Target, AppId = e.AppId }
            : new PinnedApp { Name = e.Name, Launch = "shell:AppsFolder\\" + e.Id, AppId = e.Id };

    /// <summary>True when a pin is the same app as an <see cref="AllApps"/> entry (by shortcut, app ID or program).</summary>
    public static bool IsSameApp(PinnedApp pin, AppEntry e)
    {
        if (string.Equals(pin.Launch, e.Id, StringComparison.OrdinalIgnoreCase) || string.Equals(pin.Launch, "shell:AppsFolder\\" + e.Id, StringComparison.OrdinalIgnoreCase)) return true;
        if (pin.AppId != null && e.AppId != null && string.Equals(pin.AppId, e.AppId, StringComparison.OrdinalIgnoreCase)) return true;
        return pin.Target != null && e.Target != null && string.Equals(pin.Target, e.Target, StringComparison.OrdinalIgnoreCase);
    }
}
