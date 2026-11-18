namespace Alero.Pages;

/// <summary>Cronómetro y temporizador en una sola opción, con un interruptor arriba.</summary>
public partial class TimePage : UserControl
{
    static StopwatchService W => NotchWindow.Instance.Watch;
    static TimerService T => NotchWindow.Instance.Countdown;

    // Las décimas solo se redibujan con la página a la vista; cerrado, el notch refresca una vez por segundo.
    readonly DispatcherTimer _decimals = new() { Interval = TimeSpan.FromMilliseconds(50) };
    bool _active, _timerMode;
    int _lapsShown = -1;
    string _historyShown = "";
    readonly TextBlock[] _hms = new TextBlock[3];
    readonly TextBox[] _edit = new TextBox[3];
    int _editIdx = -1;

    public TimePage()
    {
        InitializeComponent();
        _decimals.Tick += (_, _) => { if (!_timerMode) TimeText.Text = StopwatchService.Precise(W.Elapsed); };
        BuildHms();
        // Si había un temporizador a medias, se abre en esa pestaña.
        _timerMode = T.Running || (!W.Running && !W.HasTime && T.Started);
    }

    /// <summary>Muestra la parte del temporizador (para las capturas de verificación).</summary>
    public void ShowTimer() { _timerMode = true; Refresh(); }

    /// <summary>Tres columnas (horas, minutos, segundos) con + / − y la rueda del ratón.</summary>
    void BuildHms()
    {
        string[] names = { "H", "M", "S" };
        int[] steps = { 3600, 60, 1 };
        for (int i = 0; i < 3; i++)
        {
            int step = steps[i];
            var col = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0), Background = Brushes.Transparent };
            col.MouseWheel += (_, e) => { T.Adjust(e.Delta > 0 ? step : -step); e.Handled = true; };
            col.Children.Add(new TextBlock
            {
                Text = names[i], Style = (Style)FindResource("Label"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4),
            });
            col.Children.Add(StepButton("+", step));
            int idx = i;
            _hms[i] = new TextBlock
            {
                FontSize = 24, FontWeight = FontWeights.Light, FontFamily = (FontFamily)FindResource("Display"),
                HorizontalAlignment = HorizontalAlignment.Center, Cursor = Cursors.IBeam, ToolTip = "Clic para escribir",
            };
            _edit[i] = new TextBox
            {
                FontSize = 24, FontWeight = FontWeights.Light, FontFamily = (FontFamily)FindResource("Display"),
                TextAlignment = TextAlignment.Center, MaxLength = 3, BorderThickness = new Thickness(0), Padding = new Thickness(0),
                Background = Ui.Brush("Card"), Foreground = Ui.Brush("Ink"), CaretBrush = Ui.Brush("Accent"),
                VerticalContentAlignment = VerticalAlignment.Center, Visibility = Visibility.Hidden,
            };
            _edit[i].TextChanged += (_, _) => { if (_editIdx == idx) ApplyEdit(idx); };
            _edit[i].PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
            _edit[i].PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { EndEdit(idx, apply: true); e.Handled = true; }
                else if (e.Key == Key.Escape) { EndEdit(idx, apply: false); e.Handled = true; }
                else if (e.Key == Key.Tab) { EndEdit(idx, apply: true, keepTyping: true); BeginEdit((idx + 1) % 3); e.Handled = true; }
            };
            _edit[i].LostKeyboardFocus += (_, _) => EndEdit(idx, apply: true);
            var cell = new Grid { Margin = new Thickness(0, 4, 0, 4), Background = Brushes.Transparent };
            cell.Children.Add(_hms[i]);
            cell.Children.Add(_edit[i]);
            cell.MouseLeftButtonDown += (_, e) => { BeginEdit(idx); e.Handled = true; };
            col.Children.Add(cell);
            col.Children.Add(StepButton("−", -step));
            Grid.SetColumn(col, i);
            HmsPanel.Children.Add(col);
        }
    }

    void BeginEdit(int i)
    {
        if (_editIdx == i) return;
        NotchWindow.Instance.BeginTyping();
        _editIdx = i;
        _edit[i].Text = _hms[i].Text.TrimStart('0') is { Length: > 0 } t ? t : "0";
        _hms[i].Visibility = Visibility.Hidden;
        _edit[i].Visibility = Visibility.Visible;
        _edit[i].Focus();
        Keyboard.Focus(_edit[i]);
        _edit[i].SelectAll();
    }

    /// <summary>Aplica lo escrito en la casilla i al temporizador (se llama en cada tecla).</summary>
    void ApplyEdit(int i)
    {
        if (!int.TryParse(_edit[i].Text, out int v)) return;
        var t = T.Total;
        int h = (int)t.TotalHours, m = t.Minutes, s = t.Seconds;
        if (i == 0) h = v; else if (i == 1) m = v; else s = v;
        long total = h * 3600L + m * 60L + s;
        if (total >= 1 && Math.Min(total, TimerService.MaxSeconds) != (long)Math.Round(t.TotalSeconds))
            T.Set((int)Math.Min(total, TimerService.MaxSeconds));
    }

    void EndEdit(int i, bool apply, bool keepTyping = false)
    {
        if (_editIdx != i) return;
        _editIdx = -1;
        _edit[i].Visibility = Visibility.Hidden;
        _hms[i].Visibility = Visibility.Visible;
        if (apply) ApplyEdit(i);
        if (!keepTyping) NotchWindow.Instance.EndTyping();
        Refresh();
    }

    Button StepButton(string text, int seconds)
    {
        var b = new Button
        {
            Style = (Style)FindResource("PillBtn"), Content = text, FontSize = 13, Padding = new Thickness(0, 3, 0, 3),
        };
        b.Click += (_, _) => { if (_editIdx >= 0) EndEdit(_editIdx, apply: false); T.Adjust(seconds); };
        return b;
    }

    void RefreshHistory()
    {
        var hist = T.History;
        int total = (int)Math.Round(T.Total.TotalSeconds);
        string sig = string.Join(",", hist) + "|" + total;
        if (sig == _historyShown) return;
        _historyShown = sig;

        HistoryList.Children.Clear();
        foreach (int secs in hist)
        {
            bool current = secs == total;
            var b = new Button
            {
                Style = (Style)FindResource("PillBtn"), Content = Ui.Clock(TimeSpan.FromSeconds(secs)), FontSize = 12,
                Padding = new Thickness(0, 6, 0, 6), Margin = new Thickness(0, 0, 0, 4),
                Background = current ? Ui.Brush("Accent") : new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
                Foreground = Ui.Brush(current ? "OnAccent" : "Ink"),
            };
            b.Click += (_, _) => T.Set(secs);
            HistoryList.Children.Add(b);
        }
        bool empty = hist.Count == 0;
        HistoryEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ClearBtn.IsEnabled = !empty;
    }

    public void SetActive(bool active)
    {
        _active = active;
        Refresh();
    }

    public void Refresh()
    {
        ModeWatch.Background = _timerMode ? Brushes.Transparent : Ui.Brush("Card");
        ModeWatch.Foreground = Ui.Brush(_timerMode ? "Mist" : "Accent");
        ModeTimer.Background = _timerMode ? Ui.Brush("Card") : Brushes.Transparent;
        ModeTimer.Foreground = Ui.Brush(_timerMode ? "Accent" : "Mist");
        LapsPanel.Visibility = _timerMode ? Visibility.Collapsed : Visibility.Visible;
        HistoryPanel.Visibility = HmsPanel.Visibility = _timerMode ? Visibility.Visible : Visibility.Collapsed;
        LapBtn.Visibility = _timerMode ? Visibility.Collapsed : Visibility.Visible;

        bool decimalsOn = _active && !_timerMode && W.Running;
        if (decimalsOn && !_decimals.IsEnabled) _decimals.Start();
        else if (!decimalsOn && _decimals.IsEnabled) _decimals.Stop();

        if (_timerMode)
        {
            TimeText.Text = T.RemainingText;
            TimeText.Foreground = Ui.Brush(T.Running ? "Accent" : "Ink");
            StartBtn.Content = T.Running ? "Pausar" : T.Started ? "Continuar" : "Iniciar";
            ResetBtn.IsEnabled = T.Running || T.Started;
            var t = T.Total;
            _hms[0].Text = ((int)t.TotalHours).ToString();
            _hms[1].Text = t.Minutes.ToString("00");
            _hms[2].Text = t.Seconds.ToString("00");
            RefreshHistory();
        }
        else
        {
            TimeText.Text = StopwatchService.Precise(W.Elapsed);
            TimeText.Foreground = Ui.Brush(W.Running ? "Accent" : "Ink");
            StartBtn.Content = W.Running ? "Pausar" : W.HasTime ? "Continuar" : "Iniciar";
            LapBtn.IsEnabled = W.HasTime;
            ResetBtn.IsEnabled = W.HasTime;
            RefreshLaps();
        }
    }

    void RefreshLaps()
    {
        if (W.Laps.Count == _lapsShown) return;
        _lapsShown = W.Laps.Count;
        LapsEmpty.Visibility = _lapsShown == 0 ? Visibility.Visible : Visibility.Collapsed;
        Laps.Children.Clear();
        for (int i = 0; i < W.Laps.Count; i++)
        {
            // La más reciente arriba; al lado, lo que duró esa vuelta.
            var total = W.Laps[i];
            var split = total - (i + 1 < W.Laps.Count ? W.Laps[i + 1] : TimeSpan.Zero);
            var row = new Grid { Height = 24 };
            row.Children.Add(new TextBlock
            {
                Text = $"{W.Laps.Count - i}", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("Mist"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock
            {
                Text = "+" + StopwatchService.Precise(split), FontSize = 11, Foreground = Ui.Brush("Mist"),
                Margin = new Thickness(22, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock
            {
                Text = StopwatchService.Precise(total), FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = Ui.Brush(i == 0 ? "Accent" : "Ink"),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            });
            Laps.Children.Add(row);
        }
    }

    void ModeWatch_Click(object sender, RoutedEventArgs e) { _timerMode = false; Refresh(); }
    void ModeTimer_Click(object sender, RoutedEventArgs e) { _timerMode = true; Refresh(); }

    void Start_Click(object sender, RoutedEventArgs e) { if (_timerMode) T.Toggle(); else W.Toggle(); }
    void Lap_Click(object sender, RoutedEventArgs e) => W.Lap();
    void Reset_Click(object sender, RoutedEventArgs e) { if (_timerMode) T.Reset(); else W.Reset(); }
    void Clear_Click(object sender, RoutedEventArgs e) => T.ClearHistory();
}
