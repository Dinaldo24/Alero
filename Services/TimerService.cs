namespace Alero;

/// <summary>Temporizador de cuenta atrás. Sigue corriendo con el notch cerrado y avisa al terminar.</summary>
public sealed class TimerService
{
    public const int MaxSeconds = 24 * 3600 - 1;
    public const int HistorySize = 3;

    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    DateTime _endsAt;
    int _lastSecond = -1;

    public TimeSpan Total { get; private set; }
    public TimeSpan Remaining { get; private set; }
    public bool Running { get; private set; }

    /// <summary>Tiempos iniciados antes, el más reciente primero.</summary>
    public IReadOnlyList<int> History => Store.Data.TimerHistory;

    /// <summary>Cambió el estado o el segundo que se muestra.</summary>
    public event Action? Changed;
    public event Action? Finished;

    public TimerService()
    {
        int saved = Store.Data.TimerSeconds > 0 ? Store.Data.TimerSeconds : Store.Data.TimerMinutes * 60;
        Total = TimeSpan.FromSeconds(Math.Clamp(saved, 1, MaxSeconds));
        Remaining = Total;
        _tick.Tick += (_, _) => OnTick();
    }

    public double Progress => Total <= TimeSpan.Zero ? 0 : 1 - Remaining.TotalSeconds / Total.TotalSeconds;
    public bool Started => Remaining < Total;

    public void Toggle()
    {
        if (Running)
        {
            Remaining = _endsAt - DateTime.Now;
            Running = false;
            _tick.Stop();
        }
        else
        {
            if (!Started) AddHistory((int)Math.Round(Total.TotalSeconds));
            _endsAt = DateTime.Now + Remaining;
            Running = true;
            _tick.Start();
        }
        Raise(force: true);
    }

    public void Reset()
    {
        Running = false;
        _tick.Stop();
        Remaining = Total;
        Raise(force: true);
    }

    /// <summary>Elige una duración nueva y deja el temporizador listo (en pausa).</summary>
    public void Set(int seconds)
    {
        Running = false;
        _tick.Stop();
        Total = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, MaxSeconds));
        Remaining = Total;
        Remember();
        Raise(force: true);
    }

    /// <summary>Suma o resta segundos, incluso con el temporizador en marcha.</summary>
    public void Adjust(int seconds)
    {
        var current = Running ? _endsAt - DateTime.Now : Remaining;
        var delta = TimeSpan.FromSeconds(seconds);
        var newTotal = Total + delta;
        if (newTotal < TimeSpan.FromSeconds(1) || newTotal > TimeSpan.FromSeconds(MaxSeconds)) return;
        var newRemaining = current + delta;
        if (newRemaining < TimeSpan.FromSeconds(1)) return;

        Total = newTotal;
        Remaining = newRemaining;
        if (Running) _endsAt = DateTime.Now + Remaining;
        Remember();
        Raise(force: true);
    }

    public void ClearHistory()
    {
        Store.Data.TimerHistory.Clear();
        Store.Save();
        Raise(force: true);
    }

    void AddHistory(int seconds)
    {
        var h = Store.Data.TimerHistory;
        h.Remove(seconds);
        h.Insert(0, seconds);
        while (h.Count > HistorySize) h.RemoveAt(h.Count - 1);
        Store.Save();
    }

    void OnTick()
    {
        Remaining = _endsAt - DateTime.Now;
        if (Remaining <= TimeSpan.Zero)
        {
            Running = false;
            _tick.Stop();
            Remaining = Total;
            Raise(force: true);
            Finished?.Invoke();
            return;
        }
        Raise(force: false);
    }

    void Raise(bool force)
    {
        int sec = (int)Math.Ceiling(Remaining.TotalSeconds);
        if (!force && sec == _lastSecond) return; // solo se avisa cuando cambia el segundo visible
        _lastSecond = sec;
        Changed?.Invoke();
    }

    void Remember()
    {
        Store.Data.TimerSeconds = (int)Math.Round(Total.TotalSeconds);
        Store.Save();
    }

    public string RemainingText => Ui.Clock(TimeSpan.FromSeconds(Math.Ceiling(Remaining.TotalSeconds)));
}
