namespace Alero.Pages;

/// <summary>Lo que se está reproduciendo en cualquier aplicación, con controles.</summary>
public partial class PlayerPage : UserControl
{
    static NotchWindow Host => NotchWindow.Instance;
    static MediaService P => Host.Player;

    // El temporizador solo corre con el notch abierto en esta página y la música sonando.
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    bool _active;

    static bool HasMedia => P.HasSession && P.Title.Length > 0;

    public PlayerPage()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => UpdateProgress();
        SeekBar.Dragged += f => { if (P.Duration > TimeSpan.Zero) P.Seek(P.Duration * f); };
    }

    public void SetActive(bool active)
    {
        _active = active;
        if (active) Refresh();
        UpdateTimer();
    }

    void UpdateTimer()
    {
        bool run = _active && HasMedia && P.IsPlaying;
        if (run && !_timer.IsEnabled) _timer.Start();
        else if (!run && _timer.IsEnabled) _timer.Stop();
    }

    public void Refresh()
    {
        bool media = HasMedia;
        MediaView.Visibility = media ? Visibility.Visible : Visibility.Collapsed;
        EmptyView.Visibility = media ? Visibility.Collapsed : Visibility.Visible;

        if (media)
        {
            TitleText.Text = P.Title;
            ArtistText.Text = P.Artist;
            ArtistText.Visibility = P.Artist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            NotchWindow.SetArt(ArtBox, ArtGlyph, P.Art, "");
            PlayBtn.Content = P.IsPlaying ? "" : "";
            // Algunas páginas web no aceptan "siguiente"/"anterior": se atenúan para que se vea.
            NextBtn.Opacity = P.CanNext ? 1 : 0.4;
            PrevBtn.Opacity = P.CanPrevious ? 1 : 0.4;
            UpdateProgress();
        }
        UpdateTimer();
    }

    void UpdateProgress()
    {
        if (!HasMedia) return;
        var pos = P.Position;
        var dur = P.Duration;
        bool known = dur > TimeSpan.Zero;
        if (!SeekBar.IsMouseCaptured) SeekBar.Value = known ? pos.TotalSeconds / dur.TotalSeconds : 0;
        ElapsedText.Text = Ui.Clock(pos);
        RemainText.Text = known ? "-" + Ui.Clock(dur - pos) : "";
    }

    void Prev_Click(object sender, RoutedEventArgs e) => P.Previous();
    void Play_Click(object sender, RoutedEventArgs e) => P.Toggle();
    void Next_Click(object sender, RoutedEventArgs e) => P.Next();
}
