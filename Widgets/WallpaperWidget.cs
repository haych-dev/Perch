using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Perch.Core;

namespace Perch.Widgets;

/// <summary>A gallery of wallpapers Perch draws itself; click one to set it.</summary>
public sealed class WallpaperWidget : Widget
{
    public override string Id => "wallpapers";
    public override string Name => "Wallpapers";
    public override string Category => "Essentials";
    public override string Description => "A gallery of original wallpapers drawn at your screen's resolution. Can change every day.";
    public override bool IsIcon => true;
    public override double CardWidth => 452;

    static readonly Dictionary<string, BitmapSource> Thumbs = [];
    static string _thumbAccent = "";
    TextBlock? _status;
    bool _busy;

    protected override FrameworkElement BuildTile() => IconFrame(TileGlyph("", 18));

    static BitmapSource Thumb(WallpaperDesign d)
    {
        // The accent design follows the accent colour, so its thumbnail is redrawn when that changes.
        if (_thumbAccent != Settings.Current.Accent) { Thumbs.Remove("accent"); _thumbAccent = Settings.Current.Accent; }
        if (!Thumbs.TryGetValue(d.Id, out var bmp)) Thumbs[d.Id] = bmp = Wallpapers.Render(d, 256, 144);
        return bmp;
    }

    public override FrameworkElement? BuildCard()
    {
        var grid = new UniformGrid { Columns = 3 };
        foreach (var d in Wallpapers.Gallery)
        {
            var design = d;
            var img = new Image { Source = Thumb(d), Stretch = Stretch.UniformToFill, Height = 72 };
            var frame = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = img, Height = 72 };
            // Rounded corners on the picture itself.
            frame.Loaded += (_, _) => frame.Clip = new RectangleGeometry(new Rect(0, 0, frame.ActualWidth, frame.ActualHeight), 8, 8);
            frame.SizeChanged += (_, _) => frame.Clip = new RectangleGeometry(new Rect(0, 0, frame.ActualWidth, frame.ActualHeight), 8, 8);
            var name = Ui.Dim(d.Name, 11.5);
            name.Margin = new Thickness(2, 4, 0, 0);
            var cell = Ui.ListRow(Ui.Col(0, frame, name), () => Apply(design));
            cell.Padding = new Thickness(4);
            cell.ToolTip = $"Set {d.Name} as your wallpaper";
            grid.Children.Add(cell);
        }

        _status = Ui.Dim("", 12);
        var s = Settings.Current;
        var daily = Ui.SwitchLine("New wallpaper every day", "Picks a design and a fresh layout each morning", s.WallpaperDaily, on =>
        {
            Settings.Current.WallpaperDaily = on;
            Settings.SaveSoon();
        });
        var surprise = Ui.Button("Surprise me", () => Run(Wallpapers.SurpriseAsync(), "Surprise"), "Primary");
        var restore = Ui.Button("Restore my old wallpaper", () =>
        {
            try { Wallpapers.RestoreOriginal(); SetStatus("Your old wallpaper is back."); }
            catch { SetStatus("Windows didn't take it. Details are in the error log."); }
        });
        restore.IsEnabled = Wallpapers.CanRestore;
        var actions = Ui.Row(8, surprise, restore);

        return Ui.Col(10,
            Ui.Line(Ui.Title("Wallpapers"), _status),
            Ui.Scroll(grid, 380),
            Ui.Divider(),
            daily,
            actions);
    }

    void Apply(WallpaperDesign d) => Run(Wallpapers.ApplyAsync(d), d.Name);

    async void Run(Task work, string name)
    {
        if (_busy) return;
        _busy = true;
        SetStatus("Setting " + name + "…");
        Mouse.OverrideCursor = Cursors.AppStarting;
        try
        {
            await work;
            SetStatus(name == "Surprise" ? "New wallpaper set." : name + " is set.");
        }
        catch { SetStatus("That didn't work. Details are in the error log."); }
        finally
        {
            Mouse.OverrideCursor = null;
            _busy = false;
        }
    }

    void SetStatus(string text)
    {
        if (_status != null) _status.Text = text;
    }
}
