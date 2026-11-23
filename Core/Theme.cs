using System.Windows;
using System.Windows.Media;

namespace Perch.Core;

/// <summary>Swaps the shared brushes for the chosen dock style and accent colour.</summary>
public static class Theme
{
    public static readonly (string Id, string Name)[] Styles =
    [
        ("midnight", "Midnight glass"),
        ("glass", "Light glass"),
        ("black", "Black"),
        ("white", "White"),
        ("clear", "Clear"),
    ];

    public static readonly string[] Accents = ["#5AA9FF", "#8B7CFF", "#FF6FAE", "#FF8A4C", "#FFC53D", "#4CD787", "#3ED3C4", "#E8EAEE"];

    /// <summary>Styles that want the wallpaper blurred behind the bar.</summary>
    public static bool IsGlass(string style) => style is "midnight" or "glass";
    public static bool IsLight(string style) => style is "glass" or "white";

    /// <summary>
    /// Windows only draws blur when "Transparency effects" is on. With it off, the blur layer is
    /// painted as an opaque block, so the glass styles fall back to a plain see-through tint instead.
    /// </summary>
    public static bool BlurAvailable()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return (k?.GetValue("EnableTransparency") as int? ?? 1) != 0;
        }
        catch { return true; }
    }

    /// <summary>Whether the blur window should sit behind the bar.</summary>
    public static bool UsesBlur(string style) => IsGlass(style) && BlurAvailable();

    /// <summary>Acrylic tint in ABGR for the glass window.</summary>
    public static uint GlassTint(string style) => style == "glass" ? 0x55F4F4F4u : 0x6614121Au;

    public static void Apply()
    {
        var s = Settings.Current;
        bool light = IsLight(s.Style);
        var r = Application.Current.Resources;

        Color accent;
        try { accent = (Color)ColorConverter.ConvertFromString(s.Accent); } catch { accent = Color.FromRgb(0x5A, 0xA9, 0xFF); }

        bool blur = UsesBlur(s.Style);
        Set(r, "Dock.Fill", s.Style switch
        {
            // Over blur the tint can be stronger; without blur it stays light so the wallpaper shows through.
            "midnight" => blur ? Color.FromArgb(0x70, 0x10, 0x12, 0x18) : Color.FromArgb(0x4D, 0x10, 0x12, 0x18),
            "glass" => blur ? Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF),
            "black" => Color.FromArgb(0xF0, 0x0E, 0x0F, 0x13),
            "white" => Color.FromArgb(0xF2, 0xF7, 0xF7, 0xF9),
            _ => Colors.Transparent,
        });
        Set(r, "Dock.Stroke", light ? Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF) : s.Style == "clear" ? Colors.Transparent : Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        Set(r, "Dock.Text", light ? Color.FromRgb(0x16, 0x18, 0x1E) : Color.FromRgb(0xF4, 0xF5, 0xF8));
        Set(r, "Dock.Dim", light ? Color.FromRgb(0x55, 0x5C, 0x6A) : Color.FromRgb(0xA8, 0xB0, 0xBE));
        // Clear: no bar, so each tile is a light smoky pane with a fine bright edge rather than a dark box.
        Set(r, "Tile.Fill", s.Style == "clear" ? Color.FromArgb(0x40, 0x10, 0x12, 0x18) : light ? Color.FromArgb(0x1C, 0x00, 0x00, 0x00) : Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));
        Set(r, "Tile.Stroke", s.Style == "clear" ? Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF) : Colors.Transparent);
        Set(r, "Tile.Hover", s.Style == "clear" ? Color.FromArgb(0x66, 0x10, 0x12, 0x18) : light ? Color.FromArgb(0x2C, 0x00, 0x00, 0x00) : Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF));

        // Cards carry the bar's look: see-through for Clear and the glass styles (dense enough to read over
        // busy windows), solid for Black and White. Clear also gets the tiles' fine bright edge.
        Set(r, "Card.Fill", s.Style switch
        {
            "clear" => Color.FromArgb(0xC8, 0x14, 0x16, 0x1C),
            "midnight" => Color.FromArgb(0xD4, 0x14, 0x16, 0x1C),
            "glass" => Color.FromArgb(0xDC, 0xF8, 0xF8, 0xFA),
            "white" => Color.FromRgb(0xF8, 0xF8, 0xFA),
            _ => Color.FromRgb(0x1C, 0x1E, 0x25),
        });
        Set(r, "Card.Stroke", s.Style switch
        {
            "clear" => Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF),
            "glass" => Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF),
            _ when light => Color.FromArgb(0x22, 0x00, 0x00, 0x00),
            _ => Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF),
        });
        Set(r, "Card.Text", light ? Color.FromRgb(0x16, 0x18, 0x1E) : Color.FromRgb(0xF2, 0xF4, 0xF8));
        Set(r, "Card.Dim", light ? Color.FromRgb(0x60, 0x67, 0x74) : Color.FromRgb(0x9A, 0xA3, 0xB2));
        Set(r, "Card.Field", light ? Color.FromArgb(0x12, 0x00, 0x00, 0x00) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        Set(r, "Card.Hover", light ? Color.FromArgb(0x16, 0x00, 0x00, 0x00) : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        Set(r, "Card.Track", light ? Color.FromArgb(0x24, 0x00, 0x00, 0x00) : Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF));

        Set(r, "Accent", accent);
        Set(r, "AccentSoft", Color.FromArgb(0x40, accent.R, accent.G, accent.B));
        double lum = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255;
        Set(r, "OnAccent", lum > 0.7 ? Color.FromRgb(0x14, 0x16, 0x1C) : Colors.White);
    }

    static void Set(ResourceDictionary r, string key, Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        r[key] = b;
    }

    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    public static Color ColorOf(string key) => ((SolidColorBrush)Application.Current.Resources[key]).Color;
}
