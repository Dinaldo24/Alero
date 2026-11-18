using System.Diagnostics;

namespace Alero.Pages;

/// <summary>Panel de sonido: volumen general, salida de audio y volumen de cada aplicación.</summary>
public partial class SoundPage : UserControl
{
    static AudioVolume V => NotchWindow.Instance.Volume;

    // La lista de aplicaciones solo se consulta mientras esta página está a la vista.
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    string _appsKey = "";

    public SoundPage()
    {
        InitializeComponent();
        Master.Dragged += level =>
        {
            if (V.Muted && level > 0) V.SetMute(false);
            V.SetLevel(level);
            RefreshMaster();
        };
        _timer.Tick += (_, _) => { if (Mouse.Captured == null) RefreshApps(); };
    }

    public void SetActive(bool active)
    {
        if (active)
        {
            V.EnsureDevice();
            RefreshMaster();
            RefreshApps();
            _timer.Start();
        }
        else _timer.Stop();
    }

    public void RefreshMaster()
    {
        Percent.Text = V.Muted ? "Silencio" : $"{V.Level * 100:0}%";
        if (!Master.IsMouseCaptured) Master.Value = V.Muted ? 0 : V.Level;
        MuteBtn.Content = V.Muted ? "" : "";
        MuteBtn.Foreground = Ui.Brush(V.Muted ? "Accent" : "Ink");
        DeviceText.Text = V.DeviceName.Length > 0 ? V.DeviceName : "Sin dispositivo de salida";
    }

    void RefreshApps()
    {
        var sessions = V.GetSessions();
        AppsEmpty.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Si son las mismas aplicaciones con los mismos niveles, no se reconstruye nada.
        string key = string.Join("|", sessions.Select(s => $"{s.Name}:{s.Level:0.00}:{s.Active}"));
        if (key == _appsKey) return;
        _appsKey = key;

        Apps.Children.Clear();
        foreach (var s in sessions)
        {
            var row = new Grid { Height = 27 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });

            row.Children.Add(new TextBlock
            {
                Text = s.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Ui.Brush(s.Active ? "Ink" : "Mist"), Margin = new Thickness(0, 0, 8, 0),
            });

            var pct = new TextBlock
            {
                Text = $"{s.Level * 100:0}", FontSize = 11, Foreground = Ui.Brush("Mist"),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(pct, 2);
            row.Children.Add(pct);

            var slider = new Meter
            {
                Height = 20, BarHeight = 5, Value = s.Level, Cursor = Cursors.Hand,
                Fill = Ui.Brush(s.Active ? "Ink" : "Mist"), Track = Ui.Brush("Track"),
            };
            slider.Dragged += level =>
            {
                s.Set(level);
                pct.Text = $"{level * 100:0}";
                _appsKey = ""; // el siguiente refresco relee los niveles
            };
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);

            Apps.Children.Add(row);
        }
    }

    void Mute_Click(object sender, RoutedEventArgs e)
    {
        V.SetMute(!V.Muted);
        RefreshMaster();
    }

    void Device_Click(object sender, RoutedEventArgs e)
    {
        string? next = V.NextOutput();
        RefreshMaster();
        if (next == null) DeviceText.Text = "Solo hay una salida disponible";
        _appsKey = "";
        RefreshApps();
    }

    void Settings_Click(object sender, MouseButtonEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }); } catch { }
    }
}
