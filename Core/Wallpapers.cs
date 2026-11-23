using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Perch.Core;

/// <summary>One design in the gallery: a drawing style, a palette and a seed that fixes its layout.</summary>
public sealed record WallpaperDesign(string Id, string Name, string Kind, string[] Palette, int Seed);

/// <summary>
/// Original wallpapers drawn by Perch at the screen's full resolution: mesh gradients, aurora, dunes,
/// peaks, petals, orbs and silk lines. Nothing is downloaded; every image is made here.
/// </summary>
public static class Wallpapers
{
    public static readonly WallpaperDesign[] Gallery =
    [
        new("dawn", "Dawn", "mesh", ["#2A1B3D", "#FF9A8B", "#FF6A88", "#FFD3A5", "#B993D6"], 11),
        new("lagoon", "Lagoon", "mesh", ["#06203A", "#1FA2FF", "#12D8FA", "#A6FFCB", "#3A7BD5"], 23),
        new("ember", "Ember", "mesh", ["#1A0505", "#FF512F", "#DD2476", "#F09819", "#6A0572"], 37),
        new("aurora", "Aurora", "aurora", ["#030B1A", "#0B1F3A", "#3CFFB4", "#7B5CFF", "#13324F"], 5),
        new("aurora-rose", "Aurora Rose", "aurora", ["#0A0614", "#1E1030", "#FF6FD8", "#38F9D7", "#2A1840"], 17),
        new("dunes", "Dunes", "dunes", ["#FFB88C", "#DE6262", "#F3904F", "#3B4371", "#22253F"], 3),
        new("tide", "Tide", "dunes", ["#A1C4FD", "#5B86E5", "#36D1DC", "#1E3C72", "#0B1A36"], 41),
        new("peaks", "Peaks", "peaks", ["#F8B195", "#C06C84", "#6C5B7B", "#355C7D", "#1D2B45"], 8),
        new("alpine", "Alpine", "peaks", ["#E0F2FF", "#9CC9F5", "#5E8FD0", "#2E5A8F", "#13294B"], 29),
        new("bloom", "Petals", "petals", ["#04122B", "#2B6CFF", "#6FB1FF", "#B3D9FF", "#0A2A6B"], 13),
        new("silk", "Silk", "petals", ["#1C0B19", "#FF8A5B", "#FFB35C", "#FFD9A8", "#7A2E4D"], 31),
        new("orbs", "Orbs", "orbs", ["#0B0B14", "#7F5AF0", "#2CB1BC", "#FF8906", "#E53170"], 19),
        new("ribbons", "Ribbons", "lines", ["#0A0F1F", "#00C6FF", "#7F00FF", "#FF3CAC", "#2B86C5"], 7),
        new("meadow", "Meadow", "lines", ["#0E1A12", "#A8E063", "#56AB2F", "#F6D365", "#1D4D2B"], 47),
        new("accent", "Your accent", "petals", [], 53),
    ];

    /// <summary>
    /// Pictures\Perch Wallpapers. Windows refused (as "file not found") wallpapers stored in Perch's AppData
    /// folder, and Pictures is where people expect to find them anyway.
    /// </summary>
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Perch Wallpapers");

    // ───────────────────────── Rendering ─────────────────────────

    /// <summary>Draws a design as a frozen bitmap. Safe to call on any STA thread.</summary>
    public static BitmapSource Render(WallpaperDesign d, int width, int height, int? seed = null)
    {
        var palette = PaletteFor(d);
        var rnd = new Random(seed ?? d.Seed);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            switch (d.Kind)
            {
                case "aurora": DrawAurora(dc, width, height, palette, rnd); break;
                case "dunes": DrawLayers(dc, width, height, palette, rnd, jagged: false); break;
                case "peaks": DrawLayers(dc, width, height, palette, rnd, jagged: true); break;
                case "petals": DrawPetals(dc, width, height, palette, rnd); break;
                case "orbs": DrawOrbs(dc, width, height, palette, rnd); break;
                case "lines": DrawLines(dc, width, height, palette, rnd); break;
                default: DrawMesh(dc, width, height, palette, rnd); break;
            }
            DrawGrain(dc, width, height);
        }
        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    static Color[] PaletteFor(WallpaperDesign d)
    {
        if (d.Palette.Length > 0) return d.Palette.Select(Parse).ToArray();
        // "Your accent": built around the accent colour from Settings.
        // One colour family: a deep shade for the background, the accent, a slightly redder neighbour, a pale tint
        // and a dark tone for the vignette. (Mixing in other hues turns muddy where the shapes overlap.)
        var a = Parse(Settings.Current.Accent);
        return [Shade(Hue(a, -30), 0.11), a, Hue(a, -16), Mix(a, Colors.White, 0.45), Shade(Hue(a, -35), 0.32)];
    }

    static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static Color Alpha(Color c, double a) => Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B);
    static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    static Color Shade(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    static Color Hue(Color c, double degrees)
    {
        // Rotate hue in HSL space.
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, h = 0, s = 0;
        if (max != min)
        {
            double dd = max - min;
            s = l > 0.5 ? dd / (2 - max - min) : dd / (max + min);
            h = max == r ? (g - b) / dd + (g < b ? 6 : 0) : max == g ? (b - r) / dd + 2 : (r - g) / dd + 4;
            h *= 60;
        }
        h = ((h + degrees) % 360 + 360) % 360;
        double C = (1 - Math.Abs(2 * l - 1)) * s, X = C * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - C / 2;
        (double rr, double gg, double bb) = h < 60 ? (C, X, 0.0) : h < 120 ? (X, C, 0.0) : h < 180 ? (0.0, C, X) : h < 240 ? (0.0, X, C) : h < 300 ? (X, 0.0, C) : (C, 0.0, X);
        return Color.FromRgb((byte)((rr + m) * 255), (byte)((gg + m) * 255), (byte)((bb + m) * 255));
    }

    static Brush Frozen(Brush b) { b.Freeze(); return b; }

    static Brush Glow(Color c, double centreAlpha)
    {
        var g = new RadialGradientBrush { GradientStops = [new(Alpha(c, centreAlpha), 0), new(Alpha(c, centreAlpha * 0.55), 0.38), new(Alpha(c, centreAlpha * 0.18), 0.7), new(Alpha(c, 0), 1)] };
        return Frozen(g);
    }

    /// <summary>Soft overlapping colour fields, like a mesh gradient.</summary>
    static void DrawMesh(DrawingContext dc, double w, double h, Color[] p, Random r)
    {
        dc.DrawRectangle(Frozen(new LinearGradientBrush(p[0], Mix(p[0], p[4], 0.5), 35)), null, new Rect(0, 0, w, h));
        double big = Math.Max(w, h);
        for (int i = 0; i < 9; i++)
        {
            var c = p[1 + i % 4];
            double rad = big * (0.32 + r.NextDouble() * 0.38);
            var centre = new Point(w * (-0.1 + r.NextDouble() * 1.2), h * (-0.15 + r.NextDouble() * 1.3));
            dc.DrawEllipse(Glow(c, 0.72 + r.NextDouble() * 0.25), null, centre, rad, rad * (0.7 + r.NextDouble() * 0.5));
        }
        // A gentle light from the top and a darker foot give the colour field some depth. Several stops
        // spread over the full height, so neither starts at a visible line.
        dc.DrawRectangle(Frozen(new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
            GradientStops =
            [
                new(Color.FromArgb(34, 255, 255, 255), 0), new(Color.FromArgb(14, 255, 255, 255), 0.25), new(Color.FromArgb(0, 255, 255, 255), 0.5),
                new(Alpha(p[0], 0.08), 0.65), new(Alpha(p[0], 0.25), 0.85), new(Alpha(p[0], 0.4), 1),
            ],
        }), null, new Rect(0, 0, w, h));
    }

    /// <summary>Night sky with stars and curtains of light made from many faint vertical rays.</summary>
    static void DrawAurora(DrawingContext dc, double w, double h, Color[] p, Random r)
    {
        dc.DrawRectangle(Frozen(new LinearGradientBrush(p[0], p[1], 90)), null, new Rect(0, 0, w, h));
        for (int i = 0; i < (int)(w * h / 9000); i++)
        {
            double x = r.NextDouble() * w, y = r.NextDouble() * h * 0.75, s = 0.4 + r.NextDouble() * r.NextDouble() * 1.6;
            dc.DrawEllipse(Frozen(new SolidColorBrush(Color.FromArgb((byte)(60 + r.Next(170)), 255, 255, 255))), null, new Point(x, y), s * w / 2560, s * w / 2560);
        }
        for (int band = 0; band < 3; band++)
        {
            var c = band == 1 ? p[3] : p[2];
            double baseY = h * (0.42 + band * 0.09 + r.NextDouble() * 0.06);
            double amp = h * (0.06 + r.NextDouble() * 0.07), f1 = 1.2 + r.NextDouble() * 1.6, f2 = 3 + r.NextDouble() * 3, ph = r.NextDouble() * 6.28;
            double rayH = h * (0.22 + r.NextDouble() * 0.18);
            int rays = (int)(w / 3);
            double rw = w / rays * 1.6;
            for (int i = 0; i < rays; i++)
            {
                double x = i * w / rays, t = x / w;
                double y = baseY + Math.Sin(t * f1 * 6.28 + ph) * amp + Math.Sin(t * f2 * 6.28 + ph * 2) * amp * 0.3;
                double strength = 0.5 + 0.5 * Math.Sin(t * 11 + ph + band) * Math.Sin(t * 3.7 + band);
                double a = (0.05 + 0.12 * Math.Max(0, strength)) * (band == 2 ? 0.7 : 1);
                double hh = rayH * (0.6 + 0.4 * Math.Max(0, strength));
                var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), GradientStops = [new(Alpha(c, 0), 0), new(Alpha(Mix(c, p[3], 0.4), a * 0.6), 0.55), new(Alpha(c, a), 0.92), new(Alpha(c, 0), 1)] };
                dc.DrawRectangle(Frozen(brush), null, new Rect(x, y - hh, rw, hh));
            }
            dc.DrawEllipse(Glow(c, 0.16), null, new Point(w * (0.3 + band * 0.2), baseY), w * 0.45, h * 0.12);
        }
        // Low hills against the glow.
        DrawRidge(dc, w, h, h * 0.86, h * 0.05, Shade(p[4], 0.6), Shade(p[0], 0.5), r, false);
        DrawRidge(dc, w, h, h * 0.92, h * 0.04, Shade(p[0], 0.7), Color.FromRgb(2, 3, 8), r, false);
    }

    /// <summary>Layered dunes or mountain ridges fading into haze, with a low sun.</summary>
    static void DrawLayers(DrawingContext dc, double w, double h, Color[] p, Random r, bool jagged)
    {
        var sky = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), GradientStops = [new(p[0], 0), new(Mix(p[0], p[1], 0.6), 0.45), new(p[1], 0.75)] };
        dc.DrawRectangle(Frozen(sky), null, new Rect(0, 0, w, h));
        var sun = new Point(w * (0.25 + r.NextDouble() * 0.5), h * (0.3 + r.NextDouble() * 0.12));
        // The sun takes its colour from the bright top of the sky, so it reads as light, not as a grey disc.
        var sunColor = Mix(Mix(p[0], p[1], 0.25), Colors.White, 0.55);
        dc.DrawEllipse(Glow(sunColor, 0.6), null, sun, h * 0.5, h * 0.5);
        dc.DrawEllipse(Glow(Colors.White, 0.35), null, sun, h * 0.12, h * 0.12);
        dc.DrawEllipse(Frozen(new SolidColorBrush(Alpha(Mix(sunColor, Colors.White, 0.5), 0.95))), null, sun, h * 0.065, h * 0.065);
        int layers = 6;
        for (int i = 0; i < layers; i++)
        {
            double t = i / (double)(layers - 1);
            var top = Mix(Mix(p[1], p[2], 0.35), p[3], Math.Pow(t, 0.8));
            var bottom = Mix(top, p[4], 0.35 + 0.4 * t);
            if (i == layers - 1) { top = p[4]; bottom = Shade(p[4], 0.6); }
            double baseY = h * (0.42 + t * 0.42);
            double amp = h * (jagged ? 0.12 - t * 0.05 : 0.05 + t * 0.02);
            DrawRidge(dc, w, h, baseY, amp, top, bottom, r, jagged);
        }
    }

    static void DrawRidge(DrawingContext dc, double w, double h, double baseY, double amp, Color top, Color bottom, Random r, bool jagged)
    {
        int n = jagged ? 129 : 64;
        var ys = new double[n];
        if (jagged)
        {
            // Midpoint displacement for a natural ridge line.
            ys[0] = r.NextDouble() - 0.5; ys[n - 1] = r.NextDouble() - 0.5;
            double rough = 1.0;
            for (int step = (n - 1) / 2; step >= 1; step /= 2, rough *= 0.55)
                for (int i = step; i < n - 1; i += step * 2)
                    ys[i] = (ys[i - step] + ys[i + step]) / 2 + (r.NextDouble() - 0.5) * rough;
        }
        else
        {
            double f1 = 1 + r.NextDouble() * 1.5, f2 = 2.5 + r.NextDouble() * 2, ph1 = r.NextDouble() * 6.28, ph2 = r.NextDouble() * 6.28;
            for (int i = 0; i < n; i++) { double t = i / (double)(n - 1); ys[i] = Math.Sin(t * f1 * 6.28 + ph1) * 0.6 + Math.Sin(t * f2 * 6.28 + ph2) * 0.25; }
        }
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(0, h), true, true);
            var pts = Enumerable.Range(0, n).Select(i => new Point(w * i / (n - 1), baseY - ys[i] * amp * (jagged ? 2.2 : 1))).ToList();
            g.LineTo(pts[0], false, false);
            if (jagged) g.PolyLineTo(pts.Skip(1).ToList(), false, true);
            else
            {
                // Smooth curve through the points.
                for (int i = 1; i < n; i++)
                {
                    var p0 = pts[i - 1]; var p1 = pts[i];
                    var mid = new Point((p0.X + p1.X) / 2, (p0.Y + p1.Y) / 2);
                    g.QuadraticBezierTo(p0, mid, false, true);
                }
                g.LineTo(pts[n - 1], false, true);
            }
            g.LineTo(new Point(w, h), false, false);
        }
        geo.Freeze();
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), GradientStops = [new(top, 0), new(bottom, 1)] };
        dc.DrawGeometry(Frozen(fill), null, geo);
    }

    /// <summary>Large translucent petals fanning out from below, lit along their edges.</summary>
    static void DrawPetals(DrawingContext dc, double w, double h, Color[] p, Random r)
    {
        dc.DrawRectangle(Frozen(new LinearGradientBrush(p[0], p[4], 60)), null, new Rect(0, 0, w, h));
        var origin = new Point(w * (0.42 + r.NextDouble() * 0.16), h * 1.08);
        dc.DrawEllipse(Glow(p[2], 0.55), null, new Point(origin.X, h * 0.55), w * 0.5, h * 0.55);
        int petals = 9;
        double spread = 150, start = -90 - spread / 2;
        for (int i = 0; i < petals; i++)
        {
            double ang = start + spread * (i + 0.5) / petals + (r.NextDouble() - 0.5) * 8;
            double len = h * (0.85 + r.NextDouble() * 0.35), width = len * (0.22 + r.NextDouble() * 0.08);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(0, 0), true, true);
                g.BezierTo(new Point(width * 1.1, -len * 0.25), new Point(width * 0.9, -len * 0.85), new Point(0, -len), true, true);
                g.BezierTo(new Point(-width * 0.9, -len * 0.85), new Point(-width * 1.1, -len * 0.25), new Point(0, 0), true, true);
            }
            geo.Transform = new TransformGroup { Children = [new RotateTransform(ang + 90), new TranslateTransform(origin.X, origin.Y)] };
            geo.Freeze();
            var c1 = p[1 + i % 3]; var c2 = p[1 + (i + 1) % 3];
            var fill = new LinearGradientBrush { StartPoint = new Point(0.5, 1), EndPoint = new Point(0.5, 0), GradientStops = [new(Alpha(Shade(c1, 0.5), 0.0), 0), new(Alpha(c1, 0.55), 0.35), new(Alpha(Mix(c2, Colors.White, 0.25), 0.8), 1)] };
            var edge = new Pen(Frozen(new SolidColorBrush(Alpha(Mix(c2, Colors.White, 0.5), 0.35))), Math.Max(1, w / 1400));
            edge.Freeze();
            dc.DrawGeometry(Frozen(fill), edge, geo);
        }
        dc.DrawRectangle(Frozen(new RadialGradientBrush(Color.FromArgb(0, 0, 0, 0), Alpha(p[0], 0.55)) { RadiusX = 0.75, RadiusY = 0.75 }), null, new Rect(0, 0, w, h));
    }

    /// <summary>Glassy spheres floating over a dark gradient.</summary>
    static void DrawOrbs(DrawingContext dc, double w, double h, Color[] p, Random r)
    {
        dc.DrawRectangle(Frozen(new LinearGradientBrush(p[0], Mix(p[0], p[1], 0.25), 70)), null, new Rect(0, 0, w, h));
        for (int i = 0; i < 4; i++) dc.DrawEllipse(Glow(p[1 + i], 0.35), null, new Point(r.NextDouble() * w, r.NextDouble() * h), w * 0.35, w * 0.35);
        // Out-of-focus orbs far away: soft discs of light, for depth.
        for (int i = 0; i < 14; i++)
        {
            var c = p[1 + r.Next(4)];
            double rad = h * (0.03 + r.NextDouble() * 0.09);
            var centre = new Point(r.NextDouble() * w, r.NextDouble() * h);
            dc.DrawEllipse(Frozen(new RadialGradientBrush { GradientStops = [new(Alpha(c, 0.22), 0), new(Alpha(c, 0.18), 0.8), new(Alpha(c, 0), 1)] }), null, centre, rad, rad);
        }
        // A few large orbs in focus, from small at the back to one big one at the front.
        int count = 5;
        var orbs = Enumerable.Range(0, count).Select(i => (C: p[1 + i % 4], X: w * (0.1 + r.NextDouble() * 0.8), Y: h * (0.15 + r.NextDouble() * 0.7), R: h * (0.07 + 0.3 * Math.Pow((i + 0.5) / count, 1.6))))
            .OrderBy(o => o.R).ToList();
        foreach (var o in orbs)
        {
            var c = new Point(o.X, o.Y);
            dc.DrawEllipse(Glow(o.C, 0.35), null, c, o.R * 2.1, o.R * 2.1);
            var body = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.32, 0.28), Center = new Point(0.42, 0.38), RadiusX = 0.75, RadiusY = 0.75,
                GradientStops = [new(Mix(o.C, Colors.White, 0.75), 0), new(o.C, 0.38), new(Shade(o.C, 0.45), 0.85), new(Shade(o.C, 0.25), 1)],
            };
            dc.DrawEllipse(Frozen(body), null, c, o.R, o.R);
            var rim = new Pen(Frozen(new SolidColorBrush(Alpha(Mix(o.C, Colors.White, 0.6), 0.35))), Math.Max(1, o.R / 60));
            rim.Freeze();
            dc.DrawEllipse(null, rim, c, o.R, o.R);
            dc.DrawEllipse(Frozen(new RadialGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255))), null, new Point(o.X - o.R * 0.35, o.Y - o.R * 0.42), o.R * 0.32, o.R * 0.2);
        }
    }

    /// <summary>Many fine flowing lines that build up into ribbons of colour.</summary>
    static void DrawLines(DrawingContext dc, double w, double h, Color[] p, Random r)
    {
        dc.DrawRectangle(Frozen(new LinearGradientBrush(p[0], Mix(p[0], p[4], 0.35), 90)), null, new Rect(0, 0, w, h));
        dc.DrawEllipse(Glow(p[2], 0.3), null, new Point(w * 0.5, h * 0.5), w * 0.6, h * 0.5);
        for (int ribbon = 0; ribbon < 2; ribbon++)
        {
            double baseY = h * (0.38 + ribbon * 0.24 + (r.NextDouble() - 0.5) * 0.08);
            double f = 0.6 + r.NextDouble() * 0.8, ph = r.NextDouble() * 6.28, amp = h * (0.12 + r.NextDouble() * 0.08);
            int lines = 90;
            for (int i = 0; i < lines; i++)
            {
                double t = i / (double)(lines - 1);
                var stroke = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5), GradientStops = [new(Alpha(p[1 + ribbon], 0), 0), new(Alpha(p[1 + ribbon], 0.5), 0.3), new(Alpha(p[3], 0.55), 0.7), new(Alpha(p[3], 0), 1)] };
                var pen = new Pen(Frozen(stroke), Math.Max(0.8, w / 2400)) { LineJoin = PenLineJoin.Round };
                pen.Freeze();
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    int n = 80;
                    for (int k = 0; k <= n; k++)
                    {
                        double x = w * k / n, u = k / (double)n;
                        double twist = Math.Sin(u * 3.14 * 1.5 + ph) * (t - 0.5);
                        double y = baseY + Math.Sin(u * f * 6.28 + ph + t * 1.2) * amp + twist * h * 0.12 + (t - 0.5) * h * 0.05;
                        if (k == 0) g.BeginFigure(new Point(x, y), false, false); else g.LineTo(new Point(x, y), true, true);
                    }
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
            }
        }
    }

    static ImageBrush? _grain;

    /// <summary>A faint film grain over everything, which also stops gradients from banding.</summary>
    static void DrawGrain(DrawingContext dc, double w, double h)
    {
        if (_grain == null) // frozen, so it can be shared with the background render thread
        {
            const int n = 256;
            var px = new byte[n * n * 4];
            var r = new Random(1);
            for (int i = 0; i < n * n; i++)
            {
                byte v = (byte)r.Next(256), a = 14;
                byte pm = (byte)(v * a / 255);
                px[i * 4] = pm; px[i * 4 + 1] = pm; px[i * 4 + 2] = pm; px[i * 4 + 3] = a;
            }
            var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Pbgra32, null, px, n * 4);
            bmp.Freeze();
            _grain = new ImageBrush(bmp) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, n, n), ViewportUnits = BrushMappingMode.Absolute };
            _grain.Freeze();
        }
        dc.DrawRectangle(_grain, null, new Rect(0, 0, w, h));
    }

    // ───────────────────────── Applying ─────────────────────────

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool SystemParametersInfo(uint action, uint param, string value, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool SystemParametersInfo(uint action, uint param, StringBuilder value, uint flags);

    const uint SPI_SETDESKWALLPAPER = 0x14, SPI_GETDESKWALLPAPER = 0x73, SPIF_UPDATEINIFILE = 1, SPIF_SENDCHANGE = 2;

    // Windows 11's wallpaper interface (what Settings uses). The old SystemParametersInfo call reports success
    // on some builds without changing what the desktop shows, so this is tried first and checked afterwards.
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint index);
        uint GetMonitorDevicePathCount();
        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out Native.RECT rect);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(int position);
        int GetPosition();
    }

    [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    class DesktopWallpaperCom { }

    const int DWPOS_FILL = 4;

    static IDesktopWallpaper? Desktop()
    {
        try { return (IDesktopWallpaper)new DesktopWallpaperCom(); } catch { return null; }
    }

    /// <summary>The wallpaper on the first display (what the desktop actually shows).</summary>
    public static string CurrentWallpaper()
    {
        try
        {
            var dw = Desktop();
            if (dw != null && dw.GetMonitorDevicePathCount() > 0) return dw.GetWallpaper(dw.GetMonitorDevicePathAt(0)) ?? "";
        }
        catch { }
        var sb = new StringBuilder(520);
        return SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0) ? sb.ToString() : "";
    }

    /// <summary>The size to draw at: the largest display, so it is sharp on every screen.</summary>
    public static (int W, int H) TargetSize()
    {
        var all = Monitors.All();
        if (all.Count == 0) return (1920, 1080);
        var m = all.OrderByDescending(x => x.Bounds.Width * x.Bounds.Height).First();
        return (m.Bounds.Width, m.Bounds.Height);
    }

    /// <summary>Draws the design on a background thread, saves it and sets it as the desktop wallpaper.</summary>
    public static Task ApplyAsync(WallpaperDesign d, int? seed = null)
    {
        var (w, h) = TargetSize();
        string accent = Settings.Current.Accent; // read on the UI thread
        bool lockToo = Settings.Current.LockScreenToo;
        var done = new TaskCompletionSource();
        var t = new Thread(() =>
        {
            try
            {
                RememberOriginal();
                Directory.CreateDirectory(Folder);
                var bmp = Render(d, w, h, seed);
                string path = Path.Combine(Folder, $"{d.Id}-{seed ?? d.Seed}-{w}x{h}.png");
                using (var fs = File.Create(path))
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    enc.Save(fs);
                }
                SetWallpaper(path);
                if (lockToo) SetLockScreen(path);
                Cleanup(path);
                done.SetResult();
            }
            catch (Exception ex) { Log.Error(ex, "wallpaper could not be applied"); done.SetException(ex); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        _ = accent;
        return done.Task;
    }

    /// <summary>Sets the wallpaper on every display, set to Fill, and throws if Windows didn't take it.</summary>
    static void SetWallpaper(string path, string style = "10")
    {
        using (var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"))
        {
            k.SetValue("WallpaperStyle", style);
            k.SetValue("TileWallpaper", "0");
        }
        var dw = Desktop();
        if (dw != null)
        {
            try
            {
                if (style == "10") dw.SetPosition(DWPOS_FILL);
                dw.SetWallpaper(null, path);
            }
            catch (Exception ex) { Log.Error(ex, "IDesktopWallpaper refused " + path); }
        }
        if (!string.Equals(CurrentWallpaper(), path, StringComparison.OrdinalIgnoreCase))
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        if (!string.Equals(CurrentWallpaper(), path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Windows did not accept the wallpaper " + path);
    }

    /// <summary>Sets the lock screen picture (Windows' own lock screen API). Problems are logged, not thrown:
    /// the desktop wallpaper is already set by then.</summary>
    static void SetLockScreen(string path)
    {
        try
        {
            var file = Windows.Storage.StorageFile.GetFileFromPathAsync(path).AsTask().GetAwaiter().GetResult();
            Windows.System.UserProfile.LockScreen.SetImageFileAsync(file).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex) { Log.Error(ex, "the lock screen picture could not be set"); }
    }

    static string CurrentLockScreen()
    {
        try
        {
            var uri = Windows.System.UserProfile.LockScreen.OriginalImageFile;
            return uri != null && uri.IsFile ? uri.LocalPath : "";
        }
        catch { return ""; }
    }

    /// <summary>Saves the wallpaper and lock screen the user had before Perch first changed them, so they can be put back.</summary>
    static void RememberOriginal()
    {
        var s = Settings.Current;
        if (string.IsNullOrEmpty(s.OriginalLockScreen))
        {
            var lockPath = CurrentLockScreen();
            if (lockPath.Length > 0 && !lockPath.StartsWith(Folder, StringComparison.OrdinalIgnoreCase) && File.Exists(lockPath))
            {
                s.OriginalLockScreen = lockPath;
                Settings.SaveNow();
            }
        }
        if (!string.IsNullOrEmpty(s.OriginalWallpaper)) return;
        var cur = CurrentWallpaper();
        if (cur.StartsWith(Folder, StringComparison.OrdinalIgnoreCase)) return;
        s.OriginalWallpaper = cur;
        using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        s.OriginalWallpaperStyle = k?.GetValue("WallpaperStyle") as string ?? "10";
        Settings.SaveNow();
    }

    public static bool CanRestore => !string.IsNullOrEmpty(Settings.Current.OriginalWallpaper) && File.Exists(Settings.Current.OriginalWallpaper);

    public static void RestoreOriginal()
    {
        var s = Settings.Current;
        if (!CanRestore) return;
        try { SetWallpaper(s.OriginalWallpaper, string.IsNullOrEmpty(s.OriginalWallpaperStyle) ? "10" : s.OriginalWallpaperStyle); }
        catch (Exception ex) { Log.Error(ex, "the original wallpaper could not be restored"); throw; }
        if (!string.IsNullOrEmpty(s.OriginalLockScreen) && File.Exists(s.OriginalLockScreen)) SetLockScreen(s.OriginalLockScreen);
    }

    /// <summary>Keeps only the newest few images so the folder doesn't grow forever.</summary>
    static void Cleanup(string keep)
    {
        try
        {
            foreach (var f in new DirectoryInfo(Folder).GetFiles("*.png").OrderByDescending(f => f.LastWriteTime).Skip(3))
                if (!f.FullName.Equals(keep, StringComparison.OrdinalIgnoreCase)) f.Delete();
        }
        catch { }
    }

    /// <summary>Picks a random design with a fresh layout.</summary>
    public static Task SurpriseAsync()
    {
        var r = new Random();
        var d = Gallery[r.Next(Gallery.Length)];
        Settings.Current.WallpaperLastChange = DateTime.Today;
        Settings.SaveSoon();
        return ApplyAsync(d, r.Next(1, 100000));
    }

    /// <summary>Called now and then: changes the wallpaper once a day when that's switched on.</summary>
    public static void DailyCheck()
    {
        var s = Settings.Current;
        if (!s.WallpaperDaily || s.WallpaperLastChange.Date == DateTime.Today) return;
        s.WallpaperLastChange = DateTime.Today;
        _ = SurpriseAsync();
    }

    /// <summary>Saves the whole gallery as image files ("Perch.exe --export-wallpapers folder").</summary>
    public static void Export(string folder)
    {
        Directory.CreateDirectory(folder);
        var (w, h) = TargetSize();
        foreach (var d in Gallery)
        {
            var bmp = Render(d, w, h);
            using var fs = File.Create(Path.Combine(folder, $"{d.Id}.png"));
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            enc.Save(fs);
        }
    }
}
