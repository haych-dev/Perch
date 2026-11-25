using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Perch.Core;

namespace Perch.Widgets;

/// <summary>Files you opened recently, from Windows' own Recent list.</summary>
public sealed class RecentFilesWidget : Widget
{
    public override string Id => "recent";
    public override string Name => "Recent files";
    public override string Category => "Files";
    public override string Description => "Files you opened lately, from Windows' own list.";
    public override bool IsIcon => true;
    public override double CardWidth => 340;

    static string Folder => Environment.GetFolderPath(Environment.SpecialFolder.Recent);

    /// <summary>Windows' "Show recently opened items" setting (missing means on).</summary>
    static bool TrackingOn()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return (k?.GetValue("Start_TrackDocs") as int? ?? 1) != 0;
        }
        catch { return true; }
    }

    protected override FrameworkElement BuildTile() => IconFrame(TileGlyph("", 18));

    public override FrameworkElement? BuildCard()
    {
        var list = new StackPanel();
        try
        {
            var items = new DirectoryInfo(Folder).EnumerateFiles("*.lnk").OrderByDescending(f => f.LastWriteTime)
                .Select(f => (Link: f, Target: Native.ReadShortcut(f.FullName).Target))
                // Files only (folders are listed too), and only ones that still exist.
                .Where(x => x.Target != null && File.Exists(x.Target))
                .Take(10).ToList();
            foreach (var (link, target) in items)
            {
                var path = target!;
                var icon = new Image { Width = 26, Height = 26, Source = ShellIcons.Get(path) };
                var name = Ui.Text(Path.GetFileName(path), 13);
                name.MaxWidth = 250;
                var meta = Ui.Dim($"{DownloadsWidget.Ago(link.LastWriteTime)} · {Path.GetFileName(Path.GetDirectoryName(path))}", 11.5);
                meta.MaxWidth = 250;
                list.Children.Add(Ui.ListRow(Ui.Row(10, icon, Ui.Col(1, name, meta)), () => { Apps.Open(path); Host.CloseCard(); }, () => Apps.OpenFileLocation(path)));
            }
            if (items.Count == 0)
            {
                if (!TrackingOn())
                {
                    list.Children.Add(Ui.Wrap(Ui.Dim("Windows isn't keeping a list of recently opened files on this PC. Switch on \"Show recently opened items\" to fill this list.", 12.5)));
                    var open = Ui.Button("Open Start settings", () => { QuickSettings.OpenSettings("personalization-start"); Host.CloseCard(); });
                    open.Margin = new Thickness(0, 8, 0, 0);
                    list.Children.Add(open);
                }
                else list.Children.Add(Ui.Wrap(Ui.Dim("Nothing yet. Files you open will show up here.", 12.5)));
            }
        }
        catch { list.Children.Add(Ui.Dim("Windows' recent files list can't be read.", 12.5)); }
        return Ui.Col(10, Ui.Title("Recent files"), list, Ui.Dim("Right-click a file to show it in its folder.", 11.5));
    }
}

/// <summary>Your own one-click commands: scripts, programs, folders or web pages.</summary>
public sealed class CommandsWidget : Widget
{
    public override string Id => "commands";
    public override string Name => "Commands";
    public override string Category => "Productivity";
    public override string Description => "Your own one-click commands: run a script or program, open a folder or a web page.";
    public override bool IsIcon => true;
    public override double CardWidth => 360;
    bool _adding;
    TextBlock? _status;

    protected override FrameworkElement BuildTile() => IconFrame(TileGlyph("", 18));

    SavedCommand? _editing;

    public override void OnCardClosed() { _adding = false; _editing = null; }

    public override FrameworkElement? BuildCard() => _adding ? AddView(_editing) : ListView();

    void Back() { _adding = false; _editing = null; Host.RefreshCard(this); }

    FrameworkElement ListView()
    {
        var cmds = Settings.Current.Commands;
        _status = Ui.Dim("", 11.5);
        var list = new StackPanel();
        foreach (var c in cmds.ToList())
        {
            var cmd = c;
            var row = Ui.Col(1, Ui.Text(c.Name, 13, FontWeights.SemiBold), Ui.Dim(c.Command, 11.5));
            ((FrameworkElement)row).MaxWidth = 300;
            list.Children.Add(Ui.ListRow(Ui.Line(row, Ui.Glyph("", 12, "Card.Dim")), () => Run(cmd), () => Menu(cmd)));
        }
        if (cmds.Count == 0) list.Children.Add(Ui.Wrap(Ui.Dim("Save commands you run often: a script, a program with options, a folder, or a web page.", 12.5)));
        var add = Ui.Button("New command", () => { _adding = true; Host.RefreshCard(this); });
        return Ui.Col(10, Ui.Line(Ui.Title("Commands"), add), Ui.Scroll(list, 320), _status, cmds.Count > 0 ? Ui.Dim("Right-click a command to edit or remove it.", 11.5) : new Border());
    }

    FrameworkElement AddView(SavedCommand? editing = null)
    {
        var nameField = Ui.Field(out var name, editing?.Name ?? "", "Name, e.g. Clear temp files");
        var cmdField = Ui.Field(out var cmd, editing?.Command ?? "", "Command, program, folder or web address");
        name.Loaded += (_, _) => name.Focus();
        void Save()
        {
            if (cmd.Text.Trim().Length == 0) return;
            var label = name.Text.Trim().Length > 0 ? name.Text.Trim() : cmd.Text.Trim();
            if (editing != null) { editing.Name = label; editing.Command = cmd.Text.Trim(); }
            else Settings.Current.Commands.Add(new SavedCommand { Name = label, Command = cmd.Text.Trim() });
            Settings.SaveSoon();
            Back();
        }
        cmd.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); };
        var buttons = Ui.Row(8, Ui.Button("Save", Save, "Primary"), Ui.Button("Cancel", Back));
        return Ui.Col(10, Ui.Title(editing != null ? "Edit command" : "New command"), nameField, cmdField,
            Ui.Wrap(Ui.Dim("Commands run in the background, like in a Run box. Programs, folders and web addresses open normally.", 11.5)), buttons);
    }

    void Menu(SavedCommand c)
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Edit", Tag = "" }.With(() => { _editing = c; _adding = true; Host.RefreshCard(this); }));
        menu.Items.Add(new MenuItem { Header = "Remove", Tag = "" }.With(() => { Settings.Current.Commands.Remove(c); Settings.SaveSoon(); Host.RefreshCard(this); }));
        menu.IsOpen = true;
    }

    async void Run(SavedCommand c)
    {
        string text = c.Command.Trim();
        Say($"Running {c.Name}…");
        try
        {
            string expanded = Environment.ExpandEnvironmentVariables(text);
            // Web addresses, folders, files and programs open the normal way; anything else runs as a command line.
            if (Uri.TryCreate(expanded, UriKind.Absolute, out var u) && (u.Scheme is "http" or "https" or "ms-settings" or "mailto") || Directory.Exists(expanded) || File.Exists(expanded))
            {
                Apps.Open(expanded);
                Say($"Opened {c.Name}.");
                return;
            }
            var psi = new ProcessStartInfo("cmd.exe", "/c " + expanded) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
            Say(p.ExitCode == 0 ? $"{c.Name} finished." : $"{c.Name} finished with code {p.ExitCode}.");
        }
        catch (Exception ex) { Say($"{c.Name} couldn't run: {ex.Message}"); }
    }

    void Say(string s) { if (_status != null) _status.Text = s; }
}
