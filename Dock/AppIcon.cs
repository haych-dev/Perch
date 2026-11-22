using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Perch.Core;

namespace Perch.Dock;

/// <summary>One app on the dock: a pinned shortcut, a running app, or both.</summary>
public sealed class AppIcon : Grid
{
    public PinnedApp? Pin { get; private set; }
    public AppGroup? Group { get; private set; }
    public string Key { get; }

    readonly Image _image;
    readonly Border _indicator;
    readonly TextBlock _fallback;
    readonly double _size;
    string _state = "";
    readonly double _dot;

    public AppIcon(string key, double size, double padding)
    {
        Key = key;
        _size = size;
        Width = size;
        Height = size;
        ClipToBounds = false;
        // Layout inside the tile, bottom to top: a gap, the open-app dot, a gap, then the icon.
        // The space for the dot is always kept, so icons line up whether or not the app is open.
        _dot = Math.Round(Math.Max(4, size * 0.085));
        double gap = Math.Round(Math.Max(2, size * 0.05));
        double reserve = gap + _dot + gap;
        double img = Math.Round(Math.Min(size * 0.78, size - reserve - gap));
        var iconMargin = new Thickness(0, 0, 0, reserve);
        _image = new Image { Width = img, Height = img, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Bottom, Margin = iconMargin };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _fallback = Ui.Text("", size * 0.34, FontWeights.SemiBold, "Dock.Text");
        _fallback.HorizontalAlignment = HorizontalAlignment.Center;
        var fallbackTile = new Border { Width = img, Height = img, CornerRadius = new CornerRadius(img * 0.24), Child = _fallback, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Bottom, Margin = iconMargin };
        fallbackTile.SetResourceReference(Border.BackgroundProperty, "Tile.Hover");
        _indicator = new Border
        {
            Height = _dot,
            Width = _dot,
            CornerRadius = new CornerRadius(_dot / 2),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, gap),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        Children.Add(fallbackTile);
        Children.Add(_image);
        Children.Add(_indicator);
        RenderTransform = new TranslateTransform();
    }

    public string Label => Group?.Windows.Count == 1 && Pin == null && Group.Name.Length == 0 ? Group.Windows[0].Title : Pin?.Name ?? Group?.Name ?? "";
    public bool Running => Group != null && Group.Windows.Count > 0;

    public void Update(PinnedApp? pin, AppGroup? group, bool active)
    {
        bool iconChanged = Pin != pin || (Group == null) != (group == null);
        Pin = pin;
        Group = group;
        if (iconChanged || _image.Source == null)
        {
            ImageSource? src = pin != null ? ShellIcons.Get(Apps.IconSource(pin)) : null;
            src ??= group != null ? ShellIcons.Get(group.IconSource) : null;
            if (src == null && group is { Windows.Count: > 0 }) src = ShellIcons.FromWindow(group.Windows[0].Hwnd);
            _image.Source = src;
            ((Border)Children[0]).Visibility = src == null ? Visibility.Visible : Visibility.Collapsed;
            _fallback.Text = Label.Length > 0 ? Label[..1].ToUpperInvariant() : "?";
        }

        if (!Running) { _indicator.Visibility = Visibility.Collapsed; _state = ""; return; }
        _indicator.Visibility = Visibility.Visible;
        bool line = Settings.Current.Indicator == "line";
        string state = $"{active}{line}";
        if (state == _state) return;
        _state = state;
        // Same size and position for every open app; the one in front just takes the accent colour
        // (the "line" style also gets a little longer).
        _indicator.Width = line ? (active ? _size * 0.36 : _size * 0.2) : _dot;
        _indicator.Height = line ? Math.Max(3, _dot * 0.6) : _dot;
        _indicator.CornerRadius = new CornerRadius(_indicator.Height / 2);
        _indicator.SetResourceReference(Border.BackgroundProperty, active ? "Accent" : "Dock.Text");
        _indicator.Opacity = active ? 1 : 0.55;
    }
}
