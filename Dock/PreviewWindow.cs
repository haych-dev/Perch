using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Perch.Core;

namespace Perch.Dock;

/// <summary>Live thumbnails of an app's windows, shown when the pointer rests on its dock icon.</summary>
public sealed class PreviewWindow : Window
{
    IntPtr _hwnd;
    readonly List<(IntPtr Thumb, Border Slot, IntPtr Source)> _thumbs = [];
    readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    public AppGroup? Group { get; private set; }
    public event Action? Chosen;

    public PreviewWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Left = -32000;
        Top = -32000;
        Title = "Perch preview";
        SetResourceReference(BackgroundProperty, "Card.Fill");
        FontFamily = (FontFamily)Application.Current.Resources["F.Ui"];
        Content = new Border { Padding = new Thickness(8), Child = _row };
        LayoutUpdated += (_, _) => PlaceThumbs();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        int round = 2; // DWMWCP_ROUND
        Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
        int dark = Theme.IsLight(Settings.Current.Style) ? 0 : 1;
        Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
    }

    public void ShowFor(AppGroup group, Point anchorPx, bool below)
    {
        Group = group;
        Clear();
        // This window can't be see-through (live thumbnails need a normal window), so use the card colour solid.
        var c = Theme.ColorOf("Card.Fill");
        Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        double tw = 210, th = 130;
        foreach (var w in group.Windows.Take(6))
        {
            var title = Ui.Text(w.Title, 12);
            title.MaxWidth = tw - 34;
            var close = Ui.IconButton("", () => { WindowTracker.Close(w.Hwnd); Hide(); }, 10);
            close.Padding = new Thickness(6);
            var head = Ui.Line(title, close);
            var slot = new Border { Width = tw, Height = th, Margin = new Thickness(0, 6, 0, 0), Background = Brushes.Transparent };
            var tile = Ui.ListRow(Ui.Col(0, head, slot), () => { WindowTracker.Activate(w.Hwnd); Hide(); Chosen?.Invoke(); });
            tile.Margin = new Thickness(2);
            _row.Children.Add(tile);

            if (_hwnd == IntPtr.Zero) Show(); // need a handle to attach thumbnails
            if (Native.DwmRegisterThumbnail(_hwnd, w.Hwnd, out var thumb) == 0) _thumbs.Add((thumb, slot, w.Hwnd));
            else slot.Child = Ui.Dim(w.Minimized ? "Minimised" : "No preview");
        }
        Show();
        UpdateLayout();
        // Centre over the icon, above it (or below for a top dock).
        var dpi = VisualTreeHelper.GetDpi(this);
        int wpx = (int)(ActualWidth * dpi.DpiScaleX), hpx = (int)(ActualHeight * dpi.DpiScaleY);
        int x = (int)anchorPx.X - wpx / 2, y = below ? (int)anchorPx.Y + 8 : (int)anchorPx.Y - hpx - 8;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        PlaceThumbs();
    }

    void PlaceThumbs()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        foreach (var (thumb, slot, _) in _thumbs)
        {
            if (!slot.IsVisible) continue;
            var r = slot.TransformToAncestor(this).TransformBounds(new Rect(0, 0, slot.ActualWidth, slot.ActualHeight));
            Native.DwmQueryThumbnailSourceSize(thumb, out var src);
            double sw = Math.Max(1, src.cx), sh = Math.Max(1, src.cy);
            double scale = Math.Min(r.Width / sw, r.Height / sh);
            double w = sw * scale, h = sh * scale;
            double x = r.X + (r.Width - w) / 2, y = r.Y + (r.Height - h) / 2;
            var props = new Native.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = Native.DWM_TNP_RECTDESTINATION | Native.DWM_TNP_VISIBLE | Native.DWM_TNP_OPACITY | Native.DWM_TNP_SOURCECLIENTAREAONLY,
                rcDestination = new Native.RECT((int)(x * dpi.DpiScaleX), (int)(y * dpi.DpiScaleY), (int)((x + w) * dpi.DpiScaleX), (int)((y + h) * dpi.DpiScaleY)),
                opacity = 255,
                fVisible = true,
                fSourceClientAreaOnly = false,
            };
            Native.DwmUpdateThumbnailProperties(thumb, ref props);
        }
    }

    void Clear()
    {
        foreach (var (thumb, _, _) in _thumbs) Native.DwmUnregisterThumbnail(thumb);
        _thumbs.Clear();
        _row.Children.Clear();
    }

    public new void Hide()
    {
        Clear();
        Group = null;
        base.Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        Clear();
        base.OnClosed(e);
    }

    public bool PointerInside()
    {
        if (!IsVisible || _hwnd == IntPtr.Zero) return false;
        Native.GetCursorPos(out var p);
        Native.GetWindowRect(_hwnd, out var r);
        return p.X >= r.Left - 4 && p.X <= r.Right + 4 && p.Y >= r.Top - 12 && p.Y <= r.Bottom + 12;
    }
}
