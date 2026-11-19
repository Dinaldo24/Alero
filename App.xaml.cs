using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace Alero;

public partial class App : Application
{
    public static bool SnapshotMode { get; private set; }

    Mutex? _mutex;
    WF.NotifyIcon? _tray;
    NotchWindow? _win;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        int snap = Array.IndexOf(e.Args, "--snapshot");
        SnapshotMode = snap >= 0;

        if (!SnapshotMode)
        {
            _mutex = new Mutex(true, "Alero.SingleInstance", out bool isNew);
            if (!isNew)
            {
                // Tras un cierre inesperado, la copia nueva espera a que la anterior termine de morir.
                bool got = false;
                if (e.Args.Contains("--restart"))
                {
                    try { got = _mutex.WaitOne(8000); } catch (AbandonedMutexException) { got = true; }
                }
                if (!got) { Shutdown(); return; }
            }
        }

        DispatcherUnhandledException += (_, ev) => { Log(ev.Exception); ev.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, ev) => { Log(ev.Exception); ev.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            if (ev.ExceptionObject is Exception ex) Log(ex);
            if (SnapshotMode) return;
            // Si el notch se cae después de llevar un rato abierto, vuelve a levantarse solo (no si falla al arrancar, para no entrar en bucle).
            try
            {
                if ((DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalSeconds > 20 && Environment.ProcessPath is { } exe)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--restart") { UseShellExecute = false });
            }
            catch { }
        };

        Store.Load(volatileMode: SnapshotMode);
        Themes.Apply(Themes.Current);
        ClipboardService.CleanTemp();

        _win = new NotchWindow();
        _win.Show();

        if (SnapshotMode)
        {
            string dir = snap + 1 < e.Args.Length ? e.Args[snap + 1] : Path.Combine(Path.GetTempPath(), "alero-snap");
            _ = _win.RunSnapshots(dir);
        }
        else SetupTray();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        base.OnExit(e);
    }

    void SetupTray()
    {
        var menu = new WF.ContextMenuStrip();
        var start = new WF.ToolStripMenuItem("Iniciar con Windows") { Checked = Autostart.IsEnabled };
        start.Click += (_, _) => { Autostart.Set(!Autostart.IsEnabled); start.Checked = Autostart.IsEnabled; };

        var minimal = new WF.ToolStripMenuItem("Notch mínimo") { Checked = Store.Data.MinimalNotch };
        minimal.Click += (_, _) =>
        {
            Store.Data.MinimalNotch = !Store.Data.MinimalNotch;
            minimal.Checked = Store.Data.MinimalNotch;
            Store.Save();
            _win?.RefreshLayout();
        };

        var always = new WF.ToolStripMenuItem("Mantener abierto") { Checked = Store.Data.KeepOpen };
        always.Click += (_, _) =>
        {
            Store.Data.KeepOpen = !Store.Data.KeepOpen;
            always.Checked = Store.Data.KeepOpen;
            Store.Save();
            _win?.ApplyAlwaysOpen();
        };

        var colors = new WF.ToolStripMenuItem("Color");
        foreach (var pal in Themes.All)
        {
            var item = new WF.ToolStripMenuItem(pal.Name) { Checked = pal.Id == Themes.Current.Id, Image = Swatch(pal.Accent) };
            item.Click += (_, _) =>
            {
                Themes.Apply(pal);
                Store.Save();
                foreach (WF.ToolStripMenuItem o in colors.DropDownItems) o.Checked = o == item;
                _win?.SelectTab(_win.CurrentTab); // repinta lo que ya tenía el color puesto
            };
            colors.DropDownItems.Add(item);
        }


        var about = new WF.ToolStripMenuItem("Acerca de Alero");
        about.Click += (_, _) => MessageBox.Show(
            "Alero 0.4\n\nUn notch para Windows con cinco opciones:\n" +
            "  Reproductor — lo que está sonando, con controles\n" +
            "  Borrar — arrastra archivos y van a la Papelera\n" +
            "  Volumen — general, por aplicación y salida de audio\n" +
            "  Tiempo — cronómetro y temporizador\n" +
            "  Portapapeles — últimas 10 imágenes y 10 textos\n\n" +
            "Arrastra una opción del menú para cambiarla de sitio.\n" +
            "Gratis y sin cuentas. Nada sale de este equipo.",
            "Alero", MessageBoxButton.OK, MessageBoxImage.Information);

        var exit = new WF.ToolStripMenuItem("Salir");
        exit.Click += (_, _) => Shutdown();

        menu.Items.AddRange(new WF.ToolStripItem[] { start, always, minimal, colors, new WF.ToolStripSeparator(), about, exit });

        // El menú se coloca a mano (sin ContextMenuStrip del icono): con varios monitores y la barra
        // en un lateral, el automático acababa en el otro monitor.
        _tray = new WF.NotifyIcon { Icon = MakeIcon(), Text = "Alero", Visible = true };
        _tray.MouseUp += (_, ev) =>
        {
            if (ev.Button != WF.MouseButtons.Left && ev.Button != WF.MouseButtons.Right) return;
            var p = WF.Cursor.Position;
            var area = WF.Screen.FromPoint(p).WorkingArea;
            var size = menu.GetPreferredSize(SD.Size.Empty);
            // Se abre hacia el interior del monitor donde está el icono.
            int x = p.X + size.Width > area.Right ? p.X - size.Width : p.X;
            int y = p.Y + size.Height > area.Bottom ? p.Y - size.Height : p.Y;
            x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - size.Width));
            y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - size.Height));
            menu.Show(new SD.Point(x, y));
            SetForegroundWindow(menu.Handle); // para que se cierre al hacer clic fuera
        };
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    static SD.Bitmap Swatch(string hex)
    {
        var bmp = new SD.Bitmap(14, 14);
        using var g = SD.Graphics.FromImage(bmp);
        g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
        using var br = new SD.SolidBrush(SD.ColorTranslator.FromHtml(hex));
        g.FillEllipse(br, 1, 1, 12, 12);
        return bmp;
    }

    static SD.Icon MakeIcon()
    {
        // Mismo icono que el del .exe
        try { if (Environment.ProcessPath is { } exe && SD.Icon.ExtractAssociatedIcon(exe) is { } own) return own; } catch { }

        using var bmp = new SD.Bitmap(32, 32);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(SD.Color.Transparent);
            using var path = new SD.Drawing2D.GraphicsPath();
            var r = new SD.Rectangle(2, 9, 28, 14);
            int d = 14;
            path.AddArc(r.X, r.Y, d, d, 90, 180);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            path.CloseFigure();
            using var body = new SD.SolidBrush(SD.Color.White);
            g.FillPath(body, path);
            using var dot = new SD.SolidBrush(SD.Color.FromArgb(255, 0xF0, 0x8C, 0x1E));
            g.FillEllipse(dot, 19, 12, 8, 8);
        }
        return SD.Icon.FromHandle(bmp.GetHicon());
    }

    public static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Store.Dir);
            File.AppendAllText(Path.Combine(Store.Dir, "crash.log"), $"{DateTime.Now:s} {ex}\n\n");
        }
        catch { }
    }
}
