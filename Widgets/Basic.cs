using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Perch.Core;
using Perch.Dock;

namespace Perch.Widgets;

public sealed class StartWidget : Widget
{
    public override string Id => "start";
    public override string Name => "Start";
    public override string Category => "Essentials";
    public override string Description => "Opens the Start menu, like the Windows key.";
    public override bool IsIcon => true;

    protected override FrameworkElement BuildTile()
    {
        var logo = new StartLogo { Width = Size * 0.5, Height = Size * 0.5 };
        return IconFrame(logo);
    }

    public override void OnClick() => Native.PressKeys(Native.VK_LWIN);

    public override void AddMenuItems(ContextMenu menu)
    {
        menu.Items.Add(new MenuItem { Header = "Task Manager", Tag = "" }.With(() => Apps.Open("taskmgr.exe")));
        menu.Items.Add(new MenuItem { Header = "Windows Settings", Tag = "" }.With(() => QuickSettings.OpenSettings("")));
        menu.Items.Add(new MenuItem { Header = "File Explorer", Tag = "" }.With(() => Apps.Open("explorer.exe")));
        menu.Items.Add(new MenuItem { Header = "Terminal", Tag = "" }.With(() => Apps.Open("wt.exe")));
    }
}

public sealed class SeparatorWidget : Widget
{
    public override string Id => "sep";
    public override string Name => "Divider";
    public override string Category => "Essentials";
    public override string Description => "A thin line to group things on the dock.";

    protected override FrameworkElement BuildTile()
    {
        var line = new Border { Width = 2, Height = Size * 0.62, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        line.SetResourceReference(Border.BackgroundProperty, "Dock.Text");
        line.Opacity = 0.35;
        // The line is thin, but the area you can right-click or drag is a comfortable width.
        return new Grid { Height = Size, Width = Math.Max(14, 16 * Scale), Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Children = { line } };
    }

    public override void OnClick() { }
}

public sealed class ShowDesktopWidget : Widget
{
    public override string Id => "desktop";
    public override string Name => "Show desktop";
    public override string Category => "Essentials";
    public override string Description => "Minimises everything; click again to bring it back.";
    public override bool IsIcon => true;
    protected override FrameworkElement BuildTile() => IconFrame(TileGlyph("", 19));
    public override void OnClick() => QuickSettings.ToggleDesktop();
}

public sealed class ScreenshotWidget : Widget
{
    public override string Id => "screenshot";
    public override string Name => "Screenshot";
    public override string Category => "Essentials";
    public override string Description => "Snip part of the screen, a window, or all of it.";
    public override bool IsIcon => true;
    protected override FrameworkElement BuildTile() => IconFrame(TileGlyph("", 19));
    public override void OnClick() => Apps.Open("ms-screenclip:");
}

public sealed class TrashWidget : Widget
{
    public override string Id => "trash";
    public override string Name => "Recycle Bin";
    public override string Category => "Files";
    public override string Description => "Drop files here to recycle them. Right-click to empty.";
    public override bool IsIcon => true;
    public override bool AcceptsFiles => true;
    Image? _img;
    long _count = -1;

    protected override FrameworkElement BuildTile()
    {
        _img = new Image { Width = Size * 0.72, Height = Size * 0.72 };
        RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
        Tick();
        return IconFrame(_img);
    }

    public override void Tick()
    {
        var info = new Native.SHQUERYRBINFO { cbSize = Marshal.SizeOf<Native.SHQUERYRBINFO>() };
        long count = Native.SHQueryRecycleBin(null, ref info) == 0 ? info.i64NumItems : 0;
        if (count == _count || _img == null) return;
        bool changed = (count > 0) != (_count > 0);
        _count = count;
        if (changed) ShellIcons.Forget("shell:::{645FF040-5081-101B-9F08-00AA002F954E}");
        _img.Source = ShellIcons.Get("shell:::{645FF040-5081-101B-9F08-00AA002F954E}");
    }

    public override void OnClick() => Apps.Launch("shell:RecycleBinFolder");

    public override void Drop(string[] files)
    {
        Native.Recycle(files, Host.Handle);
        _count = -1;
        Tick();
    }

    public override void AddMenuItems(ContextMenu menu)
    {
        menu.Items.Add(new MenuItem { Header = "Open", Tag = "" }.With(OnClick));
        menu.Items.Add(new MenuItem { Header = "Empty Recycle Bin…", Tag = "", IsEnabled = _count > 0 }.With(() =>
        {
            Native.SHEmptyRecycleBin(Host.Handle, null, 0); // Windows asks for confirmation
            _count = -1;
            Tick();
        }));
    }
}

public sealed class DownloadsWidget : Widget
{
    public override string Id => "downloads";
    public override string Name => "Downloads";
    public override string Category => "Files";
    public override string Description => "Your newest downloads, a click away.";
    public override bool IsIcon => true;
    public override double CardWidth => 340;

    static string Folder
    {
        get
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
                var v = k?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
                if (!string.IsNullOrEmpty(v)) return Environment.ExpandEnvironmentVariables(v);
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    protected override FrameworkElement BuildTile()
    {
        var img = new Image { Width = Size * 0.72, Height = Size * 0.72, Source = ShellIcons.Get(Folder) };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        return img.Source != null ? IconFrame(img) : IconFrame(TileGlyph("", 19));
    }

    public override FrameworkElement? BuildCard()
    {
        var list = new StackPanel();
        try
        {
            var files = new DirectoryInfo(Folder).EnumerateFileSystemInfos()
                .Where(f => !f.Attributes.HasFlag(FileAttributes.Hidden) && !f.Name.EndsWith(".crdownload") && !f.Name.EndsWith(".part") && f.Name != "desktop.ini")
                .OrderByDescending(f => f.LastWriteTime).Take(9).ToList();
            foreach (var f in files)
            {
                var icon = new Image { Width = 28, Height = 28, Source = ShellIcons.Get(f.FullName) };
                string meta = f is FileInfo fi ? $"{Fmt.Bytes(fi.Length)} · {Ago(fi.LastWriteTime)}" : $"Folder · {Ago(f.LastWriteTime)}";
                var name = Ui.Text(f.Name, 13);
                name.MaxWidth = 250;
                var row = Ui.Row(10, icon, Ui.Col(1, name, Ui.Dim(meta, 11.5)));
                var path = f.FullName;
                list.Children.Add(Ui.ListRow(row, () => { Apps.Open(path); Host.CloseCard(); }, () => Apps.OpenFileLocation(path)));
            }
            if (files.Count == 0) list.Children.Add(Ui.Dim("Nothing here yet."));
        }
        catch { list.Children.Add(Ui.Dim("The Downloads folder can't be read.")); }
        var head = Ui.Line(Ui.Title("Downloads"), Ui.Button("Open folder", () => { Apps.Open(Folder); Host.CloseCard(); }));
        return Ui.Col(10, head, list);
    }

    internal static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours} h ago";
        if (d.TotalDays < 7) return $"{(int)d.TotalDays} d ago";
        return t.ToString("d MMM");
    }
}

static class MenuExtensions
{
    public static MenuItem With(this MenuItem mi, Action click)
    {
        mi.Click += (_, _) => click();
        return mi;
    }
}
