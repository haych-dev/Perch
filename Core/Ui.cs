using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Perch.Core;

/// <summary>Small builders so cards and windows can be put together in code with consistent styling.</summary>
public static class Ui
{
    public static TextBlock Text(string text, double size = 13, FontWeight? weight = null, string brush = "Card.Text")
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static TextBlock Dim(string text, double size = 12) => Text(text, size, null, "Card.Dim");

    public static TextBlock Title(string text) => Text(text, 15, FontWeights.SemiBold);

    public static TextBlock Glyph(string glyph, double size = 16, string brush = "Card.Text")
    {
        var t = Text(glyph, size, null, brush);
        t.FontFamily = (FontFamily)Application.Current.Resources["F.Icon"];
        t.TextTrimming = TextTrimming.None;
        return t;
    }

    public static StackPanel Row(double gap, params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] is FrameworkElement fe && i > 0) fe.Margin = new Thickness(gap + fe.Margin.Left, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
            p.Children.Add(children[i]);
        }
        return p;
    }

    public static StackPanel Col(double gap, params UIElement[] children)
    {
        var p = new StackPanel();
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] is FrameworkElement fe && i > 0) fe.Margin = new Thickness(fe.Margin.Left, gap + fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
            p.Children.Add(children[i]);
        }
        return p;
    }

    /// <summary>A two-column row: label on the left, control on the right.</summary>
    public static Grid Line(UIElement left, UIElement right)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(left);
        Grid.SetColumn(right, 1);
        g.Children.Add(right);
        return g;
    }

    public static Button Button(object content, Action onClick, string style = "Soft")
    {
        var b = new Button { Content = content is string s ? Text(s, 13) : content, Style = (Style)Application.Current.Resources[style] };
        if (style == "Primary" && b.Content is TextBlock tb) tb.SetResourceReference(TextBlock.ForegroundProperty, "OnAccent");
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Button IconButton(string glyph, Action onClick, double size = 16, string? tip = null)
    {
        var b = new Button { Content = Glyph(glyph, size), Style = (Style)Application.Current.Resources["Flat"], Padding = new Thickness(8), ToolTip = tip };
        b.Click += (_, _) => onClick();
        return b;
    }

    public static CheckBox Switch(bool value, Action<bool> changed)
    {
        var c = new CheckBox { IsChecked = value, Style = (Style)Application.Current.Resources["Switch"], VerticalAlignment = VerticalAlignment.Center };
        c.Checked += (_, _) => changed(true);
        c.Unchecked += (_, _) => changed(false);
        return c;
    }

    public static Grid SwitchLine(string label, string? detail, bool value, Action<bool> changed)
    {
        UIElement left = detail == null ? Text(label, 13) : Col(2, Text(label, 13), Wrap(Dim(detail)));
        return Line(left, Switch(value, changed));
    }

    public static TextBlock Wrap(TextBlock t) { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; return t; }

    public static Slider Slider(double min, double max, double value, Action<double> changed, double width = 200)
    {
        var s = new Slider { Minimum = min, Maximum = max, Value = value, Width = width, VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += (_, e) => changed(e.NewValue);
        return s;
    }

    public static Border Segmented<T>(IEnumerable<(T Value, string Label)> options, T current, Action<T> picked)
    {
        var group = Guid.NewGuid().ToString();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (value, label) in options)
        {
            var rb = new RadioButton { Content = Text(label, 12.5), GroupName = group, IsChecked = Equals(value, current), Style = (Style)Application.Current.Resources["Segment"] };
            if (rb.Content is TextBlock tb) tb.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = rb });
            var v = value;
            rb.Checked += (_, _) => picked(v);
            row.Children.Add(rb);
        }
        var b = new Border { CornerRadius = new CornerRadius(9), Child = row, HorizontalAlignment = HorizontalAlignment.Left };
        b.SetResourceReference(Border.BackgroundProperty, "Card.Field");
        return b;
    }

    /// <summary>The rounded panel that every popup card sits in.</summary>
    public static Border Card(UIElement content, double width = 320)
    {
        var b = new Border
        {
            Width = width,
            // Same rounding as the dock bar, so cards look like part of it.
            CornerRadius = new CornerRadius(Math.Clamp(Settings.Current.CornerRadius, 10, 28)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Margin = new Thickness(14),
            Child = content,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.35, Direction = 270 },
        };
        b.SetResourceReference(Border.BackgroundProperty, "Card.Fill");
        b.SetResourceReference(Border.BorderBrushProperty, "Card.Stroke");
        TextElement(b);
        return b;
    }

    static void TextElement(Border b)
    {
        System.Windows.Documents.TextElement.SetFontFamily(b, (FontFamily)Application.Current.Resources["F.Ui"]);
        b.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Card.Text");
    }

    /// <summary>A horizontal progress bar.</summary>
    public static Grid Bar(double pct, string brush = "Accent", double height = 6)
    {
        var g = new Grid { Height = height };
        var track = new Border { CornerRadius = new CornerRadius(height / 2) };
        track.SetResourceReference(Border.BackgroundProperty, "Card.Track");
        var fill = new Border { CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left };
        fill.SetResourceReference(Border.BackgroundProperty, brush);
        g.Children.Add(track);
        g.Children.Add(fill);
        g.SizeChanged += (_, _) => fill.Width = Math.Max(height, g.ActualWidth * Math.Clamp(pct, 0, 100) / 100);
        g.Tag = (Action<double>)(p => { pct = p; fill.Width = Math.Max(height, g.ActualWidth * Math.Clamp(p, 0, 100) / 100); });
        return g;
    }

    public static void SetBar(Grid bar, double pct) => (bar.Tag as Action<double>)?.Invoke(pct);

    public static Border Divider()
    {
        var b = new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4) };
        b.SetResourceReference(Border.BackgroundProperty, "Card.Stroke");
        return b;
    }

    /// <summary>A clickable row for lists inside cards.</summary>
    public static Border ListRow(UIElement content, Action? onClick, Action? onRightClick = null)
    {
        var b = new Border { Child = content, Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = onClick != null ? Cursors.Hand : null };
        b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "Card.Hover");
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        if (onClick != null) b.MouseLeftButtonUp += (_, e) => { onClick(); e.Handled = true; };
        if (onRightClick != null) b.MouseRightButtonUp += (_, e) => { onRightClick(); e.Handled = true; };
        return b;
    }

    public static ScrollViewer Scroll(UIElement content, double maxHeight) =>
        new() { Content = content, MaxHeight = maxHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    /// <summary>A text box with a placeholder shown while it is empty.</summary>
    public static Grid Field(out TextBox box, string text = "", string placeholder = "")
    {
        var t = new TextBox { Text = text, FontSize = 13 };
        var hint = Dim(placeholder, 13);
        hint.Margin = new Thickness(11, 0, 0, 0);
        hint.IsHitTestVisible = false;
        hint.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        t.TextChanged += (_, _) => hint.Visibility = t.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var g = new Grid();
        g.Children.Add(t);
        g.Children.Add(hint);
        box = t;
        return g;
    }
}
