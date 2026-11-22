using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Perch.Core;

namespace Perch.Widgets;

/// <summary>What a widget can ask of the dock it sits in.</summary>
public interface IDockHost
{
    IntPtr Handle { get; }
    bool AtTop { get; }
    void ToggleCard(Widget w);
    void OpenCard(Widget w);
    void CloseCard();
    void RefreshCard(Widget w);
    void Remove(Widget w);
    void Bounce(FrameworkElement e);
}

/// <summary>A tile on the dock. The tile is always visible; the card opens above it when the tile is clicked.</summary>
public abstract class Widget
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Category { get; }
    public abstract string Description { get; }
    /// <summary>Square, icon-sized tiles magnify like app icons and show their name on hover.</summary>
    public virtual bool IsIcon => false;
    public virtual string Glyph => "";

    public IDockHost Host { get; set; } = null!;
    public bool CardOpen { get; internal set; }

    FrameworkElement? _tile;
    public FrameworkElement Tile => _tile ??= BuildTile();

    protected abstract FrameworkElement BuildTile();
    /// <summary>The card shown above the tile; null means the widget has no card.</summary>
    public virtual FrameworkElement? BuildCard() => null;
    public virtual double CardWidth => 320;

    public virtual void OnClick() => Host.ToggleCard(this);
    public virtual void OnMiddleClick() { }
    public virtual void AddMenuItems(ContextMenu menu) { }
    public virtual void OnWheel(int delta) { }
    /// <summary>Called once a second, on the UI thread.</summary>
    public virtual void Tick() { }
    public virtual void OnCardClosed() { }
    public virtual bool AcceptsFiles => false;
    public virtual void Drop(string[] files) { }
    /// <summary>Unsubscribe from services; the tile is about to be thrown away.</summary>
    public virtual void Detach() { }

    // ───────────── Helpers for tiles ─────────────

    protected static double Size => Settings.Current.IconSize;
    protected static double Scale => Size / 44.0;

    /// <summary>The rounded tile background used by non-icon widgets.</summary>
    protected static Border Frame(UIElement content, double? width = null, double padX = 10)
    {
        var b = new Border
        {
            Height = Size,
            CornerRadius = new CornerRadius(Math.Min(14, Size * 0.3)),
            Padding = new Thickness(padX * Scale, 0, padX * Scale, 0),
            Child = content,
            ClipToBounds = true,
        };
        if (width != null) b.Width = width.Value;
        b.SetResourceReference(Border.BackgroundProperty, "Tile.Fill");
        b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "Tile.Hover");
        b.MouseLeave += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "Tile.Fill");
        return b;
    }

    /// <summary>An icon-sized square tile with a glyph or drawing in it.</summary>
    protected static Border IconFrame(UIElement content)
    {
        var b = Frame(content, Size, 0);
        if (content is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; }
        return b;
    }

    protected static TextBlock TileText(string text, double size, FontWeight? weight = null, string brush = "Dock.Text")
    {
        var t = Ui.Text(text, size * Scale, weight, brush);
        t.FontFamily = (FontFamily)Application.Current.Resources["F.Display"];
        return t;
    }

    protected static TextBlock TileGlyph(string glyph, double size = 18, string brush = "Dock.Text") => Ui.Glyph(glyph, size * Scale, brush);
}
