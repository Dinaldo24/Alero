using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace Alero;

public partial class NotchWindow : Window
{
    public static NotchWindow Instance { get; private set; } = null!;

    public enum Tab { Player, Trash, Sound, Time, Clipboard }
    enum Mode { Closed, Pill, Open }

    // Iconos de Segoe Fluent Icons
    const string IconMusic = "", IconTrash = "", IconVolume = "", IconMute = "",
                 IconWatch = "", IconPower = "", IconKeyboard = "";

    public readonly MediaService Player = new();
    public readonly AudioVolume Volume = new();
    public readonly StopwatchService Watch = new();
    public readonly TimerService Countdown = new();
    public readonly ClipboardService Clips = new();

    /// <summary>True mientras se arrastra una imagen fuera del notch (no debe cerrarse a mitad del arrastre).</summary>
    bool _appInFront;
    public bool Dragging;
    /// <summary>Imagen del historial que se está arrastrando (solo ella puede soltarse en "Borrar").</summary>
    public ClipImage? DraggedClip;

    static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        "Radius", typeof(double), typeof(NotchWindow),
        new PropertyMetadata(14.0, (d, e) => { var r = (double)e.NewValue; ((NotchWindow)d).Shell.CornerRadius = new CornerRadius(0, 0, r, r); }));

    Mode _mode = Mode.Closed;
    Tab _tab = Tab.Player;
    public Tab CurrentTab => _tab;
    bool _dragOver, _hiddenForFullscreen, _caps;
    double _targetW = 160, _targetH = 32;
    IntPtr _hwnd;
    int _tick, _lastMinute = -1;
    WF.PowerLineStatus _lastPower = WF.SystemInformation.PowerStatus.PowerLineStatus;

    readonly DispatcherTimer _openTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    readonly DispatcherTimer _closeTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    readonly DispatcherTimer _pillTimer = new();
    readonly DispatcherTimer _house = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public NotchWindow()
    {
        Instance = this;
        InitializeComponent();
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
        if (App.SnapshotMode)
        {
            // Las capturas se dibujan fuera de la pantalla para que el ratón real no interfiera.
            Left = -20000;
            IsHitTestVisible = false;
        }

        SourceInitialized += OnSourceInitialized;
        LiveBars.IsVisibleChanged += (_, _) => AnimateBars(LiveBars.IsVisible);

        Shell.MouseEnter += (_, _) => { if (App.SnapshotMode) return; _closeTimer.Stop(); _openTimer.Start(); };
        Shell.MouseLeave += (_, _) => { if (App.SnapshotMode) return; _openTimer.Stop(); ScheduleClose(); };
        HoverStrip.MouseEnter += (_, _) => { if (!App.SnapshotMode) { _closeTimer.Stop(); _openTimer.Start(); } };
        HoverStrip.MouseLeave += (_, _) => { if (!App.SnapshotMode) _openTimer.Stop(); };
        _openTimer.Tick += (_, _) => { _openTimer.Stop(); if (Shell.IsMouseOver || HoverStrip.IsMouseOver) Open(); };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            if (CursorInside() || _dragOver || Dragging || Typing) return;
            Collapse();
        };
        _pillTimer.Tick += (_, _) => { _pillTimer.Stop(); if (_mode == Mode.Pill) SetMode(Mode.Closed); };

        // Al arrastrar archivos hacia el notch se abre "Borrar"; solo se borra si se sueltan dentro del recuadro.
        Shell.DragEnter += (_, e) => { if (Dragging || !HasFiles(e)) return; // un arrastre que sale del propio notch no abre "Borrar"
 _dragOver = true; _closeTimer.Stop(); Open(Tab.Trash); };
        Shell.DragOver += (_, e) => { e.Effects = DragDropEffects.None; e.Handled = true; TrashView.Highlight(false); };
        Shell.DragLeave += (_, _) => { _dragOver = false; TrashView.Highlight(false); ScheduleClose(); };
        Shell.Drop += (_, _) => { _dragOver = false; ScheduleClose(); };

        Player.Changed += () => { UpdateClosed(); PlayerView.Refresh(); };
        Player.TrackChanged += () =>
        {
            if (Player.IsPlaying && Player.Title.Length > 0) ShowPill(IconMusic, Player.Title, Player.Artist, ms: 3200);
        };
        Watch.Changed += () => { UpdateClosed(); TimeView.Refresh(); };
        Countdown.Changed += () => { UpdateClosed(); TimeView.Refresh(); };
        Countdown.Finished += () =>
        {
            ShowPill(IconWatch, "Temporizador terminado", $"Pasaron {Ui.Clock(Countdown.Total)}", ms: 8000, force: true);
            try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
        };
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(Reposition);
        Volume.Changed += (level, muted) =>
        {
            SoundView.RefreshMaster();
            ShowPill(muted ? IconMute : IconVolume, "Volumen", null, muted ? 0 : level, muted ? "Silencio" : $"{level * 100:0}%", 1700);
        };
        _house.Tick += (_, _) => House();
        // Con una aplicación maximizada (navegador, etc.) el notch se queda mínimo; en el escritorio, normal.
        var front = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        front.Tick += (_, _) =>
        {
            bool f = Native.IsForegroundMaximized(_hwnd);
            if (f != _appInFront) { _appInFront = f; RefreshLayout(); }
        };
        Loaded += (_, _) => { if (!App.SnapshotMode) front.Start(); };

        // Con una app maximizada, los eventos de ratón de WPF fallan en el borde superior de la pantalla.
        // Se consulta el cursor directamente: si está en la franja del notch, se abre.
        int dwell = 0;
        var edge = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        edge.Tick += (_, _) =>
        {
            if (_hiddenForFullscreen || !Native.TryGetCursor(out var c)) { dwell = 0; return; }
            double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
            double cx = (Left + Width / 2) * s, half = 90 * s;
            if (_mode == Mode.Open)
            {
                // Cierre fiable: si el cursor salió del notch abierto, se cierra aunque WPF no avise.
                bool outside = c.Y > (204 + 8) * s || Math.Abs(c.X - cx) > (270 + 8) * s;
                if (outside && !_dragOver && !Dragging && !Typing && !_closeTimer.IsEnabled) ScheduleClose();
                dwell = 0;
                return;
            }
            if (_mode != Mode.Closed) { dwell = 0; return; }
            if (c.Y <= 14 * s && Math.Abs(c.X - cx) <= half) { if (++dwell >= 2) { dwell = 0; Open(); ScheduleClose(); } }
            else dwell = 0;
        };
        Loaded += (_, _) => { if (!App.SnapshotMode) edge.Start(); };

        Loaded += async (_, _) =>
        {
            HookClipTrash();
            UpdateClock();
            UpdateClosed();
            SelectTab(Tab.Player);
            if (App.SnapshotMode) return;
            if (Store.Data.KeepOpen) Open();

            _caps = (Native.GetKeyState(Native.VK_CAPITAL) & 1) != 0;
            SystemEvents.PowerModeChanged += (_, _) => Dispatcher.BeginInvoke(OnPowerChanged);
            Volume.Start();
            _house.Start();
            await Player.StartAsync();
        };
    }

    // ---- Ventana ------------------------------------------------------------------------------

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.SetExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE, true);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        if (!App.SnapshotMode) Native.AddClipboardFormatListener(_hwnd); // Windows avisa de cada copia
        Closed += (_, _) => { if (_hwnd != IntPtr.Zero) Native.RemoveClipboardFormatListener(_hwnd); };
    }

    void HookClipTrash()
    {
        var btn = Tabs.Children.Cast<Button>().First(b => (string)b.Tag == "Trash");
        btn.AllowDrop = true;
        btn.DragOver += (_, e) =>
        {
            e.Handled = true;
            bool ok = DraggedClip != null;
            e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
            btn.Opacity = ok ? 0.6 : 1;
        };
        btn.DragLeave += (_, _) => btn.Opacity = 1;
        btn.Drop += (_, e) =>
        {
            e.Handled = true; btn.Opacity = 1;
            if (DraggedClip is { } img) { var d = img; DraggedClip = null; Dispatcher.BeginInvoke(() => Clips.Remove(d)); }
        };
    }

    /// <summary>Mientras se escribe en un cuadro, el notch sí puede recibir el teclado.</summary>
    public bool Typing { get; private set; }
    IntPtr _previousForeground;

    public void BeginTyping()
    {
        if (Typing || _hwnd == IntPtr.Zero) return;
        Typing = true;
        _previousForeground = Native.Foreground();
        Native.SetExStyle(_hwnd, Native.WS_EX_NOACTIVATE, false);
        Activate();
        Native.Focus(_hwnd);
    }

    public void EndTyping()
    {
        if (!Typing) return;
        Typing = false;
        Keyboard.ClearFocus();
        if (_hwnd != IntPtr.Zero)
        {
            Native.SetExStyle(_hwnd, Native.WS_EX_NOACTIVATE, true);
            if (_previousForeground != _hwnd) Native.Focus(_previousForeground); // vuelve a la app donde estabas
        }
        if (!Shell.IsMouseOver) ScheduleClose();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_CLIPBOARDUPDATE) Clips.OnClipboardUpdate();
        if (msg == Native.WM_MOUSEACTIVATE && !Typing)
        {
            // Pasar el ratón o pulsar un botón no le quita el foco a la aplicación en la que estás.
            handled = true;
            return new IntPtr(Native.MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    // ---- Estados ------------------------------------------------------------------------------

    bool HasLive => Countdown.Running || Watch.Running || (Player.HasSession && Player.IsPlaying);

    /// <summary>Vuelve a centrar el notch arriba (cambió la resolución o el monitor, por ejemplo al salir de un juego).</summary>
    void Reposition()
    {
        if (App.SnapshotMode) return;
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
        if (_hwnd != IntPtr.Zero) { Native.EnsureShown(_hwnd); Native.KeepOnTop(_hwnd); }
    }

    public void Open(Tab? tab = null)
    {
        if (_hiddenForFullscreen) return;
        _pillTimer.Stop();
        if (tab is { } t) SelectTab(t);
        SetMode(Mode.Open);
    }

    bool CursorInside()
    {
        if (!Native.TryGetCursor(out var c)) return Shell.IsMouseOver;
        double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double cx = (Left + Width / 2) * s;
        return c.Y <= (204 + 8) * s && Math.Abs(c.X - cx) <= (270 + 8) * s;
    }

    void Collapse() { if (_mode == Mode.Open) SetMode(Mode.Closed); }

    public void ScheduleClose() { _closeTimer.Stop(); _closeTimer.Start(); }

    /// <summary>Aplica el ajuste "Mantener abierto" al momento (lo llama la bandeja).</summary>
    public void ApplyAlwaysOpen()
    {
        if (Store.Data.KeepOpen) Open();
        else { _closeTimer.Stop(); SetMode(Mode.Closed); }
    }

    void SetMode(Mode m)
    {
        // Con "Mantener abierto", el estado de reposo es abierto; solo la pantalla completa lo oculta (ver House).
        if (m == Mode.Closed && Store.Data.KeepOpen && !_hiddenForFullscreen) m = Mode.Open;
        if (_mode != m)
        {
            _mode = m;
            Fade(ClosedView, () => _mode == Mode.Closed);
            Fade(PillView, () => _mode == Mode.Pill);
            Fade(OpenView, () => _mode == Mode.Open);
            ActivatePages();
        }
        ApplySize();
    }

    /// <summary>Cada página solo trabaja (temporizadores, consultas) mientras se ve.</summary>
    void ActivatePages()
    {
        bool open = _mode == Mode.Open;
        PlayerView.SetActive(open && _tab == Tab.Player);
        SoundView.SetActive(open && _tab == Tab.Sound);
        TimeView.SetActive(open && _tab == Tab.Time);
        ClipView.SetActive(open && _tab == Tab.Clipboard);
        if (open && _tab == Tab.Trash) TrashView.Refresh();
    }

    static void Fade(FrameworkElement el, Func<bool> wanted)
    {
        if (wanted())
        {
            el.Visibility = Visibility.Visible;
            el.IsHitTestVisible = true;
            el.Anim(OpacityProperty, 1, 240, Ui.Smooth, delayMs: 30);
        }
        else
        {
            el.IsHitTestVisible = false;
            el.Anim(OpacityProperty, 0, 110, done: () => { if (!wanted()) el.Visibility = Visibility.Collapsed; });
        }
    }

    public void RefreshLayout() { _targetW = -1; ApplySize(); }

    void ApplySize()
    {
        double w, h, r;
        bool minimal = false;
        switch (_mode)
        {
            case Mode.Open:
                w = 540; h = 204; r = 28;
                break;
            case Mode.Pill:
                w = 340; h = 62; r = 22;
                break;
            default:
                minimal = Store.Data.MinimalNotch || _appInFront;
                if (minimal) { w = 150; h = 5; r = 3; }
                else { w = HasLive ? 236 : 160; h = 32; r = 14; }
                break;
        }

        if (w == _targetW && h == _targetH) return;
        bool grow = w > _targetW || h > _targetH;
        _targetW = w; _targetH = h;

        HoverStrip.Visibility = minimal ? Visibility.Visible : Visibility.Collapsed;
        EarL.Visibility = EarR.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
        int ms = grow ? 300 : 220;
        var ease = grow ? Ui.Spring : Ui.Smooth;
        Shell.Anim(WidthProperty, w, ms, ease);
        Shell.Anim(HeightProperty, h, ms, ease);
        this.Anim(RadiusProperty, r, 360, Ui.Smooth);
    }

    // ---- Opciones -----------------------------------------------------------------------------

    void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (Enum.TryParse<Tab>((string)((Button)sender).Tag, out var t)) SelectTab(t);
    }

    public void SelectTab(Tab t)
    {
        _tab = t;
        PlayerView.Visibility = Vis(t == Tab.Player);
        TrashView.Visibility = Vis(t == Tab.Trash);
        SoundView.Visibility = Vis(t == Tab.Sound);
        TimeView.Visibility = Vis(t == Tab.Time);
        ClipView.Visibility = Vis(t == Tab.Clipboard);
        foreach (Button b in Tabs.Children)
        {
            bool on = (string)b.Tag == t.ToString();
            b.Foreground = Ui.Brush(on ? "Accent" : "Mist");
            b.Background = on ? Ui.Brush("Card") : Brushes.Transparent;
        }
        ActivatePages();
        ApplySize();

        static Visibility Vis(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Vista cerrada y avisos ---------------------------------------------------------------

    /// <summary>Las barritas de música suben y bajan mientras suena; quietas (y sin gastar) cuando no se ven.</summary>
    void AnimateBars(bool on)
    {
        // (alto mínimo, alto máximo, ms) distintos para que no se muevan al unísono
        (double lo, double hi, int ms)[] cfg = { (3, 12, 420), (5, 14, 330), (3, 11, 510), (4, 13, 380) };
        for (int i = 0; i < LiveBars.Children.Count && i < cfg.Length; i++)
        {
            var bar = (UIElement)LiveBars.Children[i];
            if (!on) { bar.BeginAnimation(HeightProperty, null); continue; }
            var a = new DoubleAnimation(cfg[i].lo, cfg[i].hi, TimeSpan.FromMilliseconds(cfg[i].ms))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            bar.BeginAnimation(HeightProperty, a);
        }
    }

    void UpdateClock() => ClockText.Text = DateTime.Now.ToString("HH:mm");

    void UpdateClosed()
    {
        // La hora siempre queda en el centro; a los lados va lo que esté en marcha.
        ClockText.Visibility = Visibility.Visible;
        if (Countdown.Running)
        {
            LiveView.Visibility = Visibility.Visible;
            SetArt(LiveArt, LiveGlyph, null, IconWatch);
            LiveText.Text = Countdown.RemainingText; LiveText.Visibility = Visibility.Visible;
            LiveBars.Visibility = Visibility.Collapsed;
        }
        else if (Watch.Running)
        {
            LiveView.Visibility = Visibility.Visible;
            SetArt(LiveArt, LiveGlyph, null, IconWatch);
            LiveText.Text = Ui.Clock(Watch.Elapsed); LiveText.Visibility = Visibility.Visible;
            LiveBars.Visibility = Visibility.Collapsed;
        }
        else if (Player.HasSession && Player.IsPlaying)
        {
            LiveView.Visibility = Visibility.Visible;
            SetArt(LiveArt, LiveGlyph, Player.Art, IconMusic);
            LiveText.Visibility = Visibility.Collapsed;
            LiveBars.Visibility = Visibility.Visible;
        }
        else LiveView.Visibility = Visibility.Collapsed;
        ApplySize();
    }

    public static void SetArt(Border box, TextBlock glyph, ImageSource? art, string fallback)
    {
        if (art != null)
        {
            box.Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
            glyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            box.Background = Ui.Brush("Card");
            glyph.Text = fallback;
            glyph.Visibility = Visibility.Visible;
        }
    }

    /// <param name="force">Interrumpe el notch abierto (para avisos importantes, como el fin del temporizador).</param>
    public void ShowPill(string glyph, string title, string? sub, double? progress = null, string? right = null, int ms = 2800, bool force = false)
    {
        if ((_mode == Mode.Open && !force) || _hiddenForFullscreen) return;

        SetArt(PillIcon, PillGlyph, glyph == IconMusic ? Player.Art : null, glyph);
        PillTitle.Text = title;
        PillSub.Text = sub ?? "";
        PillSub.Visibility = string.IsNullOrEmpty(sub) ? Visibility.Collapsed : Visibility.Visible;
        PillBar.Visibility = progress.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (progress.HasValue) PillBar.Value = progress.Value;
        PillRight.Text = right ?? "";

        SetMode(Mode.Pill);
        _pillTimer.Stop();
        _pillTimer.Interval = TimeSpan.FromMilliseconds(ms);
        _pillTimer.Start();
    }

    // ---- Borrar -------------------------------------------------------------------------------

    /// <summary>La página "Borrar" lo llama cuando se sueltan archivos en su recuadro.</summary>
    public void TrashFiles(string[] files)
    {
        _dragOver = false;
        int n = Trash.Recycle(files, _hwnd);
        string what = files.Length == 1 ? Path.GetFileName(files[0].TrimEnd('\\')) : $"{n} elementos";
        TrashView.SetStatus(n > 0 ? $"Enviado a la Papelera: {what}" : "No se borró nada");
        TrashView.Refresh();

        // Se cierra y confirma con un aviso, para que quede claro qué pasó.
        SetMode(Mode.Closed);
        if (n > 0) ShowPill(IconTrash, "Enviado a la Papelera", what, ms: 3000);
        else ShowPill(IconTrash, "No se borró nada", "El archivo sigue en su sitio", ms: 3000);
    }

    static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    // ---- Tareas periódicas --------------------------------------------------------------------

    void House()
    {
        _tick++;

        bool caps = (Native.GetKeyState(Native.VK_CAPITAL) & 1) != 0;
        if (caps != _caps)
        {
            _caps = caps;
            ShowPill(IconKeyboard, "Bloq Mayús", caps ? "Activado" : "Desactivado", ms: 1500);
        }

        if (_tick % 3 == 0)
        {
            bool fs = Native.IsForegroundFullscreen(_hwnd);
            if (fs != _hiddenForFullscreen)
            {
                _hiddenForFullscreen = fs;
                if (fs) { _pillTimer.Stop(); SetMode(Mode.Closed); }
                Root.Visibility = fs ? Visibility.Collapsed : Visibility.Visible;
                if (!fs)
                {
                    // Terminó el juego o el video: el notch vuelve solo, en su sitio y abierto si así está configurado.
                    Reposition();
                    SetMode(Mode.Closed); // con "Mantener abierto" pasa directamente a abierto
                    if (Store.Data.KeepOpen) Open();
                }
            }
            if (!fs) Native.KeepOnTop(_hwnd);
        }

        if (_tick % 10 == 0) Volume.EnsureDevice();

        int minute = DateTime.Now.Minute;
        if (minute != _lastMinute) { _lastMinute = minute; UpdateClock(); }
    }

    void OnPowerChanged()
    {
        var ps = WF.SystemInformation.PowerStatus;
        if (ps.PowerLineStatus == _lastPower) return;
        _lastPower = ps.PowerLineStatus;
        string pct = ps.BatteryLifePercent <= 1 ? $"{ps.BatteryLifePercent * 100:0}%" : "";
        if (ps.PowerLineStatus == WF.PowerLineStatus.Online) ShowPill(IconPower, "Cargando", "Cargador conectado", right: pct);
        else if (ps.PowerLineStatus == WF.PowerLineStatus.Offline) ShowPill(IconPower, "Con batería", "Cargador desconectado", right: pct);
    }

    // ---- Capturas de verificación (Alero.exe --snapshot <carpeta>) -----------------------------

    public async Task RunSnapshots(string dir)
    {
        Directory.CreateDirectory(dir);
        async Task Snap(string name) { await Task.Delay(1100); SaveSnapshot(Path.Combine(dir, name + ".png")); }
        var log = new List<string>();
        Store.Data.KeepOpen = false; // las capturas de "cerrado" necesitan que pueda cerrarse

        try
        {
            await Snap("01-cerrado");
            Open(Tab.Player); await Snap("02-reproductor-vacio");

            SetMode(Mode.Closed);
            Player.SetDemo(); await Snap("03-cerrado-musica");
            ShowPill(IconMusic, Player.Title, Player.Artist); await Snap("04-aviso-cancion");
            ShowPill(IconVolume, "Volumen", null, 0.64, "64%"); await Snap("05-aviso-volumen");
            Open(Tab.Player); await Snap("06-reproductor");
            Open(Tab.Trash); await Snap("08-borrar");
            TrashView.Highlight(true); await Snap("09-borrar-arrastrando");
            TrashView.Highlight(false);

            Volume.Start();
            Open(Tab.Sound); await Snap("10-volumen");

            Open(Tab.Time);
            Watch.Toggle(); await Task.Delay(1300); Watch.Lap(); await Task.Delay(900); Watch.Lap();
            await Snap("11-cronometro");
            Watch.Reset();
            TimeView.ShowTimer(); Countdown.Set(300); Countdown.Toggle();
            await Snap("12-temporizador");
            SetMode(Mode.Closed); await Snap("13-cerrado-temporizador");
            Countdown.Reset();

            // Portapapeles: historial de imágenes y de texto.
            await Clips.AddDemo();
            Open(Tab.Clipboard); await Snap("13b-portapapeles-imagenes");
            ClipView.ShowText(); await Snap("13c-portapapeles-texto");
            // Más de 10 de cada uno: solo deben quedar 10.
            for (int i = 0; i < 14; i++) Clips.AddText("texto de prueba " + i);
            log.Add($"portapapeles: textos={Clips.Texts.Count} (máx {ClipboardService.Max}) · imágenes={Clips.Images.Count}");

            // Papelera: un archivo de prueba debe dejar de existir en su carpeta y aparecer el aviso.
            string victim = Path.Combine(dir, "prueba-para-borrar.txt");
            File.WriteAllText(victim, "archivo temporal de prueba de Alero");
            var before = Trash.Query();
            TrashFiles(new[] { victim });
            var after = Trash.Query();
            log.Add($"papelera: archivo sigue existiendo={File.Exists(victim)} · elementos antes={before.items} después={after.items}");
            await Snap("14-aviso-borrado");

            // Sonido: lectura del dispositivo, las aplicaciones y la orden de cambio de salida (a la misma salida: no cambia nada).
            var outputs = Volume.GetOutputs();
            var sessions = Volume.GetSessions();
            log.Add($"sonido: salida actual=\"{Volume.DeviceName}\" · nivel={Volume.Level * 100:0}% · silencio={Volume.Muted}");
            log.Add($"sonido: salidas={outputs.Count} [{string.Join(" | ", outputs.Select(o => o.name))}]");
            log.Add($"sonido: aplicaciones={sessions.Count} [{string.Join(" | ", sessions.Select(s => $"{s.Name} {s.Level * 100:0}%"))}]");
            if (Volume.CurrentOutputId is { } id) log.Add($"sonido: Windows aceptó fijar la salida={Volume.SetOutput(id)}");
        }
        catch (Exception ex) { log.Add("ERROR: " + ex); }

        File.WriteAllLines(Path.Combine(dir, "pruebas.txt"), log);
        Application.Current.Shutdown();
    }

    void SaveSnapshot(string file)
    {
        const double scale = 2;
        double w = Stage.ActualWidth, h = Stage.ActualHeight;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x5B, 0x7C, 0x99)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(Stage) { Stretch = Stretch.None, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Top },
                             null, new Rect(0, 0, w, h));
        }
        var bmp = new RenderTargetBitmap((int)(w * scale), (int)(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
