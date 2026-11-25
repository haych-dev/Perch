#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Perch.Core;

namespace Perch.Widgets;

/// <summary>
/// Debug builds only: "Perch.exe --render-widgets folder [id,id,…]" draws each widget's tile and card into PNG
/// files, so they can be reviewed without opening the dock. Settings are read-only while it runs.
/// </summary>
public static class RenderCheck
{
    sealed class NoHost : IDockHost
    {
        public IntPtr Handle => IntPtr.Zero;
        public bool AtTop => false;
        public void ToggleCard(Widget w) { }
        public void OpenCard(Widget w) { }
        public void CloseCard() { }
        public void RefreshCard(Widget w) { }
        public void Remove(Widget w) { }
        public void Bounce(FrameworkElement e) { }
    }

    public static async Task Run(string folder, string[] ids)
    {
        Settings.ReadOnly = true;
        Directory.CreateDirectory(folder);
        var ui = Application.Current.Dispatcher;
        SystemSampler.Instance.Start(ui);
        Media.Instance.Start(ui);
        Weather.Instance.Start();
        // Public test accounts, in memory only.
        Settings.Current.GitHubUser = "torvalds";
        Settings.Current.LeetCodeUser = "lee215";
        if (Settings.Current.WorldClocks.Count == 0) Settings.Current.WorldClocks = ["GMT Standard Time", "Pacific Standard Time"];

        var host = new NoHost();
        var widgets = ids.Select(id => (id, w: WidgetRegistry.Create(id))).Where(x => x.w != null).ToList();
        var tiles = new List<(string Id, Widget W, FrameworkElement Tile)>();
        foreach (var (id, w) in widgets)
        {
            w!.Host = host;
            try { tiles.Add((id, w, w.Tile)); } catch (Exception ex) { File.WriteAllText(Path.Combine(folder, id + "-error.txt"), ex.ToString()); }
        }
        await Task.Delay(6000); // let samplers and web requests come back
        for (int i = 0; i < 60 && ids.Contains("claudecode") && ClaudeUsage.Instance.Current == null; i++) await Task.Delay(1000);
        foreach (var (id, w, tile) in tiles)
        {
            try { w.Tick(); } catch { }
            Save(Wrap(tile, false), Path.Combine(folder, id + "-tile.png"));
            try
            {
                w.CardOpen = true;
                var first = w.BuildCard();
                if (first == null) continue;
                await Task.Delay(2500); // cards that load more when opened
                var card = w.BuildCard() ?? first;
                Save(Wrap(Ui.Card(card, w.CardWidth), true), Path.Combine(folder, id + "-card.png"));
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(folder, id + "-card-error.txt"), ex.ToString()); }
            finally { w.CardOpen = false; }
        }
        foreach (var (_, w, _) in tiles) { try { w.Detach(); } catch { } }
    }

    static FrameworkElement Wrap(FrameworkElement e, bool card)
    {
        // Detach from any earlier parent, then sit it on a dark wallpaper-like background.
        if (e.Parent is Panel p) p.Children.Remove(e);
        else if (e.Parent is Border b) b.Child = null;
        var bg = new Border
        {
            Padding = new Thickness(card ? 4 : 12),
            Background = new LinearGradientBrush(Color.FromRgb(0x23, 0x2E, 0x4A), Color.FromRgb(0x0E, 0x12, 0x1C), 90),
            Child = e,
        };
        TextOptions.SetTextFormattingMode(bg, TextFormattingMode.Ideal);
        return bg;
    }

    static void Save(FrameworkElement e, string path)
    {
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        e.Arrange(new Rect(e.DesiredSize));
        e.UpdateLayout();
        const double scale = 1.5;
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(e.ActualWidth * scale), (int)Math.Ceiling(e.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(e);
        using var fs = File.Create(path);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        enc.Save(fs);
    }
}
#endif
