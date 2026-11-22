using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Perch.Core;

namespace Perch;

/// <summary>Base for Perch's ordinary windows (settings, widget picker): themed, with a matching title bar.</summary>
public class PerchWindow : Window
{
    public PerchWindow()
    {
        FontFamily = (FontFamily)Application.Current.Resources["F.Ui"];
        SetResourceReference(BackgroundProperty, "Card.Fill");
        SetResourceReference(ForegroundProperty, "Card.Text");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        UseLayoutRounding = true;
        Settings.Changed += _ => ApplyTitleBar();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyTitleBar();
    }

    void ApplyTitleBar()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        int dark = Theme.IsLight(Settings.Current.Style) ? 0 : 1;
        Native.DwmSetWindowAttribute(h, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        // Solid background colour; Mica would fight the custom card colours.
        var c = Theme.ColorOf("Card.Fill");
        int colorRef = c.R | (c.G << 8) | (c.B << 16);
        Native.DwmSetWindowAttribute(h, 35 /* DWMWA_CAPTION_COLOR */, ref colorRef, 4);
        Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
    }

    protected static TextBlock Section(string text)
    {
        var t = Ui.Text(text, 12, FontWeights.SemiBold, "Card.Dim");
        t.Margin = new Thickness(0, 18, 0, 2);
        return t;
    }

    protected static Border Group(params UIElement[] rows)
    {
        var col = new StackPanel();
        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0) col.Children.Add(Ui.Divider());
            if (rows[i] is FrameworkElement fe) fe.Margin = new Thickness(0, 6, 0, 6);
            col.Children.Add(rows[i]);
        }
        var b = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 6, 16, 6), Child = col, Margin = new Thickness(0, 6, 0, 0) };
        b.SetResourceReference(Border.BackgroundProperty, "Card.Field");
        return b;
    }
}
