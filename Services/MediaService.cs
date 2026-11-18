using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Manager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace Alero;

/// <summary>
/// Lee "lo que suena" desde el control multimedia de Windows (el mismo que usa la superposición de volumen),
/// así funciona con Spotify, navegadores, reproductores locales, etc. Windows avisa de los cambios: no hay sondeo.
/// </summary>
public sealed class MediaService
{
    public event Action? Changed;
    public event Action? TrackChanged;

    public bool HasSession { get; private set; }
    public bool IsPlaying { get; private set; }
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public ImageSource? Art { get; private set; }
    public TimeSpan Duration { get; private set; }

    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    Manager? _mgr;
    Session? _session;
    TimeSpan _pos;
    DateTime _posAt = DateTime.UtcNow;
    double _rate = 1;
    int _ver;

    public TimeSpan Position
    {
        get
        {
            var p = _pos;
            if (IsPlaying) p += (DateTime.UtcNow - _posAt) * _rate;
            if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
            return p < TimeSpan.Zero ? TimeSpan.Zero : p;
        }
    }

    public async Task StartAsync()
    {
        try
        {
            _mgr = await Manager.RequestAsync();
            _mgr.CurrentSessionChanged += (_, _) => _ui.BeginInvoke(Attach);
            Attach();
        }
        catch { /* Sin control multimedia disponible: la página de inicio muestra el reloj. */ }
    }

    void Attach()
    {
        if (_session != null)
        {
            try
            {
                _session.MediaPropertiesChanged -= OnProps;
                _session.PlaybackInfoChanged -= OnPlayback;
                _session.TimelinePropertiesChanged -= OnTimeline;
            }
            catch { }
        }

        try { _session = _mgr?.GetCurrentSession(); } catch { _session = null; }
        _ver++;

        if (_session == null)
        {
            bool had = Title.Length > 0;
            HasSession = false; IsPlaying = false; Title = ""; Artist = ""; Art = null; Duration = TimeSpan.Zero;
            Changed?.Invoke();
            if (had) TrackChanged?.Invoke();
            return;
        }

        _session.MediaPropertiesChanged += OnProps;
        _session.PlaybackInfoChanged += OnPlayback;
        _session.TimelinePropertiesChanged += OnTimeline;
        HasSession = true;
        ReadPlayback();
        ReadTimeline();
        _ = ReadPropsAsync();
    }

    void OnProps(Session s, MediaPropertiesChangedEventArgs e) => _ui.BeginInvoke(() => { _ = ReadPropsAsync(); });
    void OnPlayback(Session s, PlaybackInfoChangedEventArgs e) => _ui.BeginInvoke(() => { ReadPlayback(); Changed?.Invoke(); });
    void OnTimeline(Session s, TimelinePropertiesChangedEventArgs e) => _ui.BeginInvoke(() => { ReadTimeline(); Changed?.Invoke(); });

    void ReadPlayback()
    {
        try
        {
            var info = _session?.GetPlaybackInfo();
            if (info == null) return;
            bool playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            if (playing != IsPlaying)
            {
                // Congela (o reanuda) la posición estimada justo en el cambio de estado.
                _pos = Position;
                _posAt = DateTime.UtcNow;
                IsPlaying = playing;
            }
            _rate = info.PlaybackRate ?? 1;
            CanNext = info.Controls.IsNextEnabled;
            CanPrevious = info.Controls.IsPreviousEnabled;
        }
        catch { }
    }

    void ReadTimeline()
    {
        try
        {
            var t = _session?.GetTimelineProperties();
            if (t == null) return;
            Duration = t.EndTime - t.StartTime;
            _pos = t.Position;
            var at = t.LastUpdatedTime.UtcDateTime;
            _posAt = at.Year > 2000 && at <= DateTime.UtcNow ? at : DateTime.UtcNow;
        }
        catch { }
    }

    async Task ReadPropsAsync()
    {
        var s = _session;
        if (s == null) return;
        int ver = ++_ver;
        try
        {
            var p = await s.TryGetMediaPropertiesAsync();
            if (ver != _ver || p == null) return;

            string title = p.Title ?? "", artist = p.Artist ?? "";
            bool changed = title != Title || artist != Artist;
            Title = title; Artist = artist;
            if (changed) Art = null;
            Changed?.Invoke();
            if (changed) TrackChanged?.Invoke();

            var art = await LoadArt(p.Thumbnail);
            if (ver != _ver || art == null) return;
            Art = art;
            Changed?.Invoke();
        }
        catch { }
    }

    static async Task<ImageSource?> LoadArt(IRandomAccessStreamReference? r)
    {
        if (r == null) return null;
        try
        {
            using var s = await r.OpenReadAsync();
            using var net = s.AsStreamForRead();
            var ms = new MemoryStream();
            await net.CopyToAsync(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 220;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>Si la página/aplicación declaró que acepta "siguiente" / "anterior" (muchas páginas web no lo hacen).</summary>
    public bool CanNext { get; private set; } = true;
    public bool CanPrevious { get; private set; } = true;

    public async void Toggle() { try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { } }

    public async void Next()
    {
        bool ok = false;
        try { if (_session != null) ok = await _session.TrySkipNextAsync(); } catch { }
        // Si la fuente no lo acepta, se prueba con la tecla multimedia del teclado.
        if (!ok) Native.MediaKey(Native.VK_MEDIA_NEXT_TRACK);
    }

    public async void Previous()
    {
        // Como en cualquier reproductor: pasados unos segundos, "anterior" vuelve al inicio de la canción.
        if (Position > TimeSpan.FromSeconds(4) && Duration > TimeSpan.Zero) { Seek(TimeSpan.Zero); return; }
        bool ok = false;
        try { if (_session != null) ok = await _session.TrySkipPreviousAsync(); } catch { }
        if (!ok) Native.MediaKey(Native.VK_MEDIA_PREV_TRACK);
    }

    public async void Seek(TimeSpan to)
    {
        if (to < TimeSpan.Zero) to = TimeSpan.Zero;
        try
        {
            if (_session == null) return;
            if (await _session.TryChangePlaybackPositionAsync(to.Ticks))
            {
                _pos = to; _posAt = DateTime.UtcNow;
                Changed?.Invoke();
            }
        }
        catch { }
    }

    /// <summary>Datos de muestra para las capturas de verificación.</summary>
    public void SetDemo()
    {
        HasSession = true; IsPlaying = true; _rate = 0;
        Title = "Costa de papel"; Artist = "Los Faros · Verano lento";
        Duration = TimeSpan.FromSeconds(256); _pos = TimeSpan.FromSeconds(193);
        Changed?.Invoke();
    }
}
