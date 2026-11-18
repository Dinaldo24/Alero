using System.Diagnostics;

namespace Alero;

/// <summary>Cronómetro con vueltas. Sigue contando aunque el notch esté cerrado.</summary>
public sealed class StopwatchService
{
    readonly Stopwatch _sw = new();
    // Con el notch cerrado basta con refrescar una vez por segundo.
    readonly DispatcherTimer _second = new() { Interval = TimeSpan.FromSeconds(1) };

    public List<TimeSpan> Laps { get; } = new();
    public bool Running => _sw.IsRunning;
    public TimeSpan Elapsed => _sw.Elapsed;
    public bool HasTime => _sw.Elapsed > TimeSpan.Zero;

    /// <summary>Cambió el estado (iniciar, pausar, vuelta, reiniciar) o pasó un segundo.</summary>
    public event Action? Changed;

    public StopwatchService() => _second.Tick += (_, _) => Changed?.Invoke();

    public void Toggle()
    {
        if (_sw.IsRunning) { _sw.Stop(); _second.Stop(); }
        else { _sw.Start(); _second.Start(); }
        Changed?.Invoke();
    }

    public void Lap()
    {
        if (!HasTime) return;
        Laps.Insert(0, _sw.Elapsed);
        Changed?.Invoke();
    }

    public void Reset()
    {
        _sw.Reset();
        _second.Stop();
        Laps.Clear();
        Changed?.Invoke();
    }

    /// <summary>mm:ss.d — con décimas, para la página.</summary>
    public static string Precise(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}"
                          : $"{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}";
}
