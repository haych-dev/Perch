using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Perch.Core;

namespace Perch.Widgets;

public sealed class NowPlayingWidget : Widget
{
    public override string Id => "nowplaying";
    public override string Name => "Now playing";
    public override string Category => "Media";
    public override string Description => "What's playing in Spotify, your browser or any media app, with controls.";
    public override double CardWidth => 300;

    Border? _art;
    TextBlock? _title, _artist, _play;
    Border? _frame;

    public NowPlayingWidget() { Media.Instance.Changed += Update; }
    public override void Detach() => Media.Instance.Changed -= Update;

    protected override FrameworkElement BuildTile()
    {
        _art = new Border { Width = Size * 0.72, Height = Size * 0.72, CornerRadius = new CornerRadius(7 * Scale), ClipToBounds = true };
        _art.SetResourceReference(Border.BackgroundProperty, "Tile.Hover");
        _title = TileText("", 12, FontWeights.SemiBold);
        _artist = TileText("", 10.5, null, "Dock.Dim");
        var text = Ui.Col(0, _title, _artist);
        text.Width = 110 * Scale;
        _play = TileGlyph("", 15);
        var playBtn = new Border { Child = _play, Padding = new Thickness(6), Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
        playBtn.MouseLeftButtonUp += (_, e) => { Media.Instance.PlayPause(); e.Handled = true; };
        _frame = Frame(Ui.Row(8 * Scale, _art, text, playBtn), null, 6);
        Update();
        return _frame;
    }

    void Update()
    {
        var np = Media.Instance.Current;
        if (_title == null || _artist == null || _art == null || _play == null) return;
        _title.Text = np?.Title is { Length: > 0 } t ? t : "Nothing playing";
        _artist.Text = np == null ? "Play something" : np.Artist.Length > 0 ? np.Artist : np.App;
        _art.Child = np?.Art != null ? new Image { Source = np.Art, Stretch = Stretch.UniformToFill } : CenterGlyph("", 14 * Scale, "Dock.Dim");
        _play.Text = np?.Playing == true ? "" : "";
        if (CardOpen) Host.RefreshCard(this);
    }

    static FrameworkElement CenterGlyph(string g, double size, string brush)
    {
        var t = Ui.Glyph(g, size, brush);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return t;
    }

    public override void OnWheel(int delta)
    {
        var v = Audio.Speakers();
        if (v.Available) Audio.SetVolume(v.Level + (delta > 0 ? 0.04f : -0.04f));
    }

    public override FrameworkElement? BuildCard()
    {
        var np = Media.Instance.Current;
        var art = new Border { Height = 268, CornerRadius = new CornerRadius(12), ClipToBounds = true };
        art.SetResourceReference(Border.BackgroundProperty, "Card.Field");
        art.Child = np?.Art != null ? new Image { Source = np.Art, Stretch = Stretch.UniformToFill } : CenterGlyph("", 48, "Card.Dim");
        var title = Ui.Text(np?.Title is { Length: > 0 } t ? t : "Nothing playing", 15, FontWeights.SemiBold);
        var artist = Ui.Dim(np == null ? "Start music or a video in any app." : $"{np.Artist}{(np.App.Length > 0 ? " · " + np.App : "")}", 12.5);
        var prev = Ui.IconButton("", Media.Instance.Previous, 18);
        var play = Ui.IconButton(np?.Playing == true ? "" : "", Media.Instance.PlayPause, 24);
        var next = Ui.IconButton("", Media.Instance.Next, 18);
        prev.IsEnabled = np?.CanPrevious ?? false;
        next.IsEnabled = np?.CanNext ?? false;
        var controls = Ui.Row(18, prev, play, next);
        controls.HorizontalAlignment = HorizontalAlignment.Center;
        var vol = Audio.Speakers();
        var slider = Ui.Slider(0, 100, vol.Level * 100, v => Audio.SetVolume((float)(v / 100)), 220);
        var volRow = Ui.Row(8, Ui.Glyph("", 14, "Card.Dim"), slider);
        volRow.HorizontalAlignment = HorizontalAlignment.Center;
        return Ui.Col(12, art, Ui.Col(2, title, artist), controls, vol.Available ? volRow : new Border());
    }
}

public sealed class VolumeWidget : Widget
{
    public override string Id => "volume";
    public override string Name => "Sound";
    public override string Category => "Media";
    public override string Description => "Volume and mute for speakers and microphone. Scroll on it to change the volume.";
    public override bool IsIcon => true;

    TextBlock? _glyph;
    Slider? _slider;
    TextBlock? _pct;

    protected override FrameworkElement BuildTile()
    {
        _glyph = TileGlyph("", 19);
        Tick();
        return IconFrame(_glyph);
    }

    static string GlyphFor(Audio.State s) => !s.Available || s.Muted || s.Level < 0.005 ? "" : s.Level < 0.34 ? "" : s.Level < 0.67 ? "" : "";

    public override void Tick()
    {
        var s = Audio.Speakers();
        if (_glyph != null) _glyph.Text = GlyphFor(s);
        if (CardOpen && _pct != null) _pct.Text = $"{s.Level * 100:0}%";
    }

    public override void OnWheel(int delta)
    {
        var s = Audio.Speakers();
        if (!s.Available) return;
        Audio.SetVolume(s.Level + (delta > 0 ? 0.04f : -0.04f));
        Tick();
    }

    public override void OnMiddleClick()
    {
        var s = Audio.Speakers();
        Audio.SetMute(!s.Muted);
        Tick();
    }

    public override FrameworkElement? BuildCard()
    {
        var spk = Audio.Speakers();
        var mic = Audio.Microphone();
        _pct = Ui.Text($"{spk.Level * 100:0}%", 13, FontWeights.SemiBold);
        _pct.Width = 38;
        _slider = Ui.Slider(0, 100, spk.Level * 100, v => { Audio.SetVolume((float)(v / 100)); Tick(); }, 190);
        var mute = Ui.IconButton(GlyphFor(spk), () => { Audio.SetMute(!Audio.Speakers().Muted); Host.RefreshCard(this); Tick(); }, 16, "Mute");
        var col = Ui.Col(12,
            Ui.Title("Sound"),
            Ui.Row(4, mute, _slider, _pct),
            Ui.Dim(spk.Available ? spk.Device : "No speakers found", 11.5));
        if (mic.Available)
        {
            col.Children.Add(Ui.Divider());
            col.Children.Add(Ui.SwitchLine("Microphone on", mic.Device, !mic.Muted, on => Audio.SetMute(!on, microphone: true)));
        }
        col.Children.Add(Ui.Row(8,
            Ui.Button("Volume mixer", () => { QuickSettings.OpenSettings("apps-volume"); Host.CloseCard(); }),
            Ui.Button("Sound settings", () => { QuickSettings.OpenSettings("sound"); Host.CloseCard(); })));
        return col;
    }
}
