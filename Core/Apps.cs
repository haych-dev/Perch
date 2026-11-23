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

    // ───────────── All apps (what Start lists, Store apps included) ─────────────
    static List<(string Name, string Id)>? _allApps;
    static DateTime _allAppsAt;

    /// <summary>
    /// Every app in Start's "All apps" list, read from the shell's Applications folder. Each id is either an
    /// app user model ID (Store and many desktop apps) or a known-folder path to an exe. Call on the UI thread.
    /// </summary>
    public static List<(string Name, string Id)> AllApps()
    {
        if (_allApps != null && DateTime.Now - _allAppsAt < TimeSpan.FromMinutes(5)) return _allApps;
        var list = new List<(string, string)>();
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
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
                    if (id.StartsWith("http", StringComparison.OrdinalIgnoreCase) || name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add((name, id));
                }
            }
        }
        catch (Exception ex) { Log.Error(ex, "the All apps list could not be read"); }
        if (list.Count == 0) list = StartMenuApps().Select(a => (a.Name, a.Path)).ToList(); // fallback
        _allApps = list.GroupBy(a => a.Item1, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(a => a.Item1).ToList();
        _allAppsAt = DateTime.Now;
        return _allApps;
    }

    /// <summary>A pin for an entry from <see cref="AllApps"/>.</summary>
    public static PinnedApp FromAllApps(string name, string id)
    {
        if (File.Exists(id) || id.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return FromFile(id) ?? new PinnedApp { Name = name, Launch = id };
        string? exe = ResolveKnownFolderPath(id);
        bool isAppId = exe == null && !id.Contains('\\');
        return new PinnedApp { Name = name, Launch = "shell:AppsFolder\\" + id, AppId = isAppId ? id : null, Target = exe };
    }

    /// <summary>True when a pin is the same app as an <see cref="AllApps"/> entry.</summary>
    public static bool IsSameApp(PinnedApp pin, string id)
    {
        if (string.Equals(pin.Launch, "shell:AppsFolder\\" + id, StringComparison.OrdinalIgnoreCase) || string.Equals(pin.Launch, id, StringComparison.OrdinalIgnoreCase)) return true;
        if (pin.AppId != null && string.Equals(pin.AppId, id, StringComparison.OrdinalIgnoreCase)) return true;
        var exe = ResolveKnownFolderPath(id);
        return exe != null && pin.Target != null && string.Equals(pin.Target, exe, StringComparison.OrdinalIgnoreCase);
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    /// <summary>Turns "{known-folder-guid}\sub\app.exe" into a real path, or null if it isn't one.</summary>
    static string? ResolveKnownFolderPath(string id)
    {
        if (!id.StartsWith('{')) return null;
        int close = id.IndexOf('}');
        if (close < 0 || !Guid.TryParse(id[..(close + 1)], out var guid)) return null;
        if (SHGetKnownFolderPath(guid, 0, IntPtr.Zero, out var p) != 0) return null;
        try { return Path.Combine(System.Runtime.InteropServices.Marshal.PtrToStringUni(p) ?? "", id[(close + 1)..].TrimStart('\\')); }
        finally { System.Runtime.InteropServices.Marshal.FreeCoTaskMem(p); }
    }
}
