using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;

namespace Perch.Core;

public sealed class NowPlaying
{
    public string Title = "", Artist = "", App = "";
    public bool Playing;
    public ImageSource? Art;
    public bool CanNext, CanPrevious;
    public TimeSpan Position, Duration;
}

/// <summary>Whatever is playing (Spotify, browsers, media players), via the Windows media session that drives the volume flyout.</summary>
public sealed class Media
{
    public static Media Instance { get; } = new();
    public event Action? Changed;
    public NowPlaying? Current { get; private set; }

    GlobalSystemMediaTransportControlsSessionManager? _mgr;
    GlobalSystemMediaTransportControlsSession? _session;
    Dispatcher? _ui;
    string _artKey = "";
    bool _started;

    public async void Start(Dispatcher ui)
    {
        if (_started) return;
        _started = true;
        _ui = ui;
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _mgr.CurrentSessionChanged += (_, _) => Attach();
            _mgr.SessionsChanged += (_, _) => Attach();
            Attach();
        }
        catch (Exception ex) { Debug.WriteLine("Media sessions unavailable: " + ex.Message); }
    }

    void Attach()
    {
        var s = _mgr?.GetCurrentSession() ?? _mgr?.GetSessions().FirstOrDefault();
        if (!ReferenceEquals(s, _session))
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnChanged;
                _session.PlaybackInfoChanged -= OnChanged;
                _session.TimelinePropertiesChanged -= OnChanged;
            }
            _session = s;
            if (_session != null)
            {
                _session.MediaPropertiesChanged += OnChanged;
                _session.PlaybackInfoChanged += OnChanged;
                _session.TimelinePropertiesChanged += OnChanged;
            }
        }
        _ = ReadAsync();
    }

    void OnChanged(GlobalSystemMediaTransportControlsSession s, object e) => _ = ReadAsync();

    async Task ReadAsync()
    {
        var s = _session;
        if (s == null) { Publish(null); return; }
        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            var pb = s.GetPlaybackInfo();
            var tl = s.GetTimelineProperties();
            var np = new NowPlaying
            {
                Title = props?.Title ?? "",
                Artist = props?.Artist is { Length: > 0 } a ? a : props?.AlbumArtist ?? "",
                App = AppName(s.SourceAppUserModelId),
                Playing = pb?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                CanNext = pb?.Controls.IsNextEnabled ?? false,
                CanPrevious = pb?.Controls.IsPreviousEnabled ?? false,
                Position = tl?.Position ?? TimeSpan.Zero,
                Duration = tl != null ? tl.EndTime - tl.StartTime : TimeSpan.Zero,
            };

            string key = np.Title + "|" + np.Artist;
            if (key == _artKey && Current != null) np.Art = Current.Art;
            else if (props?.Thumbnail != null)
            {
                try
                {
                    using var stream = await props.Thumbnail.OpenReadAsync();
                    using var net = stream.AsStreamForRead();
                    var ms = new MemoryStream();
                    await net.CopyToAsync(ms);
                    ms.Position = 0;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 160;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    np.Art = bmp;
                }
                catch { }
            }
            _artKey = key;
            Publish(np.Title.Length == 0 && !np.Playing ? null : np);
        }
        catch { Publish(null); }
    }

    void Publish(NowPlaying? np) => _ui?.BeginInvoke(() => { Current = np; Changed?.Invoke(); });

    static string AppName(string aumid)
    {
        if (string.IsNullOrEmpty(aumid)) return "";
        var s = aumid;
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = Path.GetFileNameWithoutExtension(s);
        int bang = s.IndexOf('!');
        if (bang > 0) s = s[..bang];
        int us = s.IndexOf('_');
        if (us > 0) s = s[..us];
        int dot = s.LastIndexOf('.');
        if (dot >= 0 && dot < s.Length - 1) s = s[(dot + 1)..];
        return s.Length > 0 ? char.ToUpper(s[0]) + s[1..] : s;
    }

    public async void PlayPause() { try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { } }
    public async void Next() { try { if (_session != null) await _session.TrySkipNextAsync(); } catch { } }
    public async void Previous() { try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { } }
}
