using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Perch.Core;

namespace Perch.Widgets;

/// <summary>Microphone on/off in one click.</summary>
public sealed class MicWidget : Widget
{
    public override string Id => "mic";
    public override string Name => "Microphone";
    public override string Category => "Media";
    public override string Description => "Mute and unmute your microphone in one click. Turns red while muted.";
    public override bool IsIcon => true;
    TextBlock? _glyph;
    Border? _frame;

    protected override FrameworkElement BuildTile()
    {
        _glyph = TileGlyph("", 18);
        _frame = IconFrame(_glyph);
        Tick();
        return _frame;
    }

    public override void Tick()
    {
        var m = Audio.Microphone();
        if (_glyph == null) return;
        _glyph.Text = !m.Available ? "" : m.Muted ? "" : "";
        _glyph.SetResourceReference(TextBlock.ForegroundProperty, m.Available && m.Muted ? "Bad" : m.Available ? "Dock.Text" : "Dock.Dim");
    }

    public override void OnClick()
    {
        var m = Audio.Microphone();
        if (!m.Available) return;
        Audio.SetMute(!m.Muted, microphone: true);
        Tick();
    }
}

/// <summary>Pick which speakers or headphones play sound.</summary>
public sealed class AudioOutputWidget : Widget
{
    public override string Id => "audioout";
    public override string Name => "Sound output";
    public override string Category => "Media";
    public override string Description => "Switch between speakers, headphones and other outputs.";
    public override double CardWidth => 320;
    TextBlock? _name;

    protected override FrameworkElement BuildTile()
    {
        _name = TileText("", 11.5, FontWeights.SemiBold);
        _name.MaxWidth = 100 * Scale;
        Tick();
        return Frame(Ui.Row(7 * Scale, TileGlyph("", 16), Ui.Col(0, TileText("Output", 9.5, null, "Dock.Dim"), _name)), null, 10);
    }

    int _tick;
    public override void Tick()
    {
        if (_name == null || (_tick++ % 5 != 0 && _name.Text.Length > 0)) return;
        var d = Audio.Outputs().FirstOrDefault(x => x.IsDefault);
        _name.Text = d.Name is { Length: > 0 } n ? Short(n) : "None";
    }

    /// <summary>"Speakers (Realtek(R) Audio)" → "Speakers".</summary>
    static string Short(string n)
    {
        int p = n.IndexOf(" (", StringComparison.Ordinal);
        return p > 0 ? n[..p] : n;
    }

    public override FrameworkElement? BuildCard()
    {
        var list = new StackPanel();
        foreach (var d in Audio.Outputs())
        {
            var dev = d;
            var mark = Ui.Glyph(d.IsDefault ? "" : "", 13, "Accent");
            mark.Width = 18;
            var label = Ui.Text(d.Name, 13, d.IsDefault ? FontWeights.SemiBold : FontWeights.Normal);
            label.MaxWidth = 250;
            list.Children.Add(Ui.ListRow(Ui.Row(6, mark, label), () =>
            {
                if (Audio.SetDefaultOutput(dev.Id)) { _tick = 0; _name!.Text = ""; Tick(); Host.RefreshCard(this); }
            }));
        }
        if (list.Children.Count == 0) list.Children.Add(Ui.Dim("No sound outputs found.", 12.5));
        return Ui.Col(10, Ui.Title("Play sound through"), Ui.Scroll(list, 340), Ui.Button("Sound settings", () => { QuickSettings.OpenSettings("sound"); Host.CloseCard(); }));
    }
}

/// <summary>Just the album art; click for the player card, middle-click to play or pause.</summary>
public sealed class NowPlayingMiniWidget : Widget
{
    public override string Id => "nowplayingmini";
    public override string Name => "Now playing (small)";
    public override string Category => "Media";
    public override string Description => "Album art in a single tile. Click for controls, middle-click to play or pause.";
    public override bool IsIcon => true;
    public override double CardWidth => 300;
    readonly NowPlayingWidget _full = new();
    Border? _art;
    TextBlock? _state;

    public NowPlayingMiniWidget() { Media.Instance.Changed += Update; }
    public override void Detach() { Media.Instance.Changed -= Update; _full.Detach(); }

    protected override FrameworkElement BuildTile()
    {
        _art = new Border { Width = Size * 0.8, Height = Size * 0.8, CornerRadius = new CornerRadius(Size * 0.18), ClipToBounds = true };
        _state = TileGlyph("", 10);
        _state.HorizontalAlignment = HorizontalAlignment.Right;
        _state.VerticalAlignment = VerticalAlignment.Bottom;
        var g = new Grid { Width = Size, Height = Size, Children = { _art } };
        _art.HorizontalAlignment = HorizontalAlignment.Center;
        _art.VerticalAlignment = VerticalAlignment.Center;
        Update();
        return g;
    }

    void Update()
    {
        if (_art == null) return;
        var np = Media.Instance.Current;
        if (np?.Art != null) _art.Child = new Image { Source = np.Art, Stretch = Stretch.UniformToFill };
        else
        {
            var glyph = TileGlyph("", 18, "Dock.Dim");
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            _art.Child = glyph;
            _art.SetResourceReference(Border.BackgroundProperty, "Tile.Fill");
        }
        _art.Opacity = np?.Playing == true || np == null ? 1 : 0.6;
        if (CardOpen) Host.RefreshCard(this);
    }

    public override void OnMiddleClick() => Media.Instance.PlayPause();

    public override FrameworkElement? BuildCard() { _full.Host = Host; _full.CardOpen = true; return _full.BuildCard(); }
    public override void OnCardClosed() => _full.CardOpen = false;
}
