using System.Windows.Media.Animation;

namespace Alero;

internal static class Ui
{
    public static readonly IEasingFunction Spring = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.42 });
    public static readonly IEasingFunction Smooth = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    public static FontFamily IconFont => (FontFamily)Application.Current.Resources["Icons"];

    public static void Anim(this UIElement el, DependencyProperty dp, double to, int ms,
                            IEasingFunction? ease = null, int delayMs = 0, Action? done = null)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        if (delayMs > 0) a.BeginTime = TimeSpan.FromMilliseconds(delayMs);
        if (done != null) a.Completed += (_, _) => done();
        el.BeginAnimation(dp, a);
    }

    public static string Clock(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
    }

    public static string Ago(DateTime when)
    {
        var d = DateTime.Now - when;
        if (d.TotalSeconds < 45) return "ahora";
        if (d.TotalMinutes < 60) return $"hace {(int)Math.Max(1, d.TotalMinutes)} min";
        if (d.TotalHours < 24) return $"hace {(int)d.TotalHours} h";
        return when.ToString("d MMM");
    }

    public static string Size(long bytes)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{v:0} {u[i]}" : $"{v:0.#} {u[i]}";
    }
}

/// <summary>
/// Barra de progreso mínima: solo se redibuja cuando cambia su valor.
/// Si alguien escucha <see cref="Dragged"/>, además funciona como deslizador.
/// </summary>
public class Meter : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(Meter), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(Meter), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(Meter), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BarHeightProperty = DependencyProperty.Register(
        nameof(BarHeight), typeof(double), typeof(Meter), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    /// <summary>Grosor de la barra cuando el control es más alto (para que sea fácil de agarrar).</summary>
    public double BarHeight { get => (double)GetValue(BarHeightProperty); set => SetValue(BarHeightProperty, value); }

    /// <summary>Se dispara mientras el usuario arrastra, con el nuevo valor entre 0 y 1.</summary>
    public event Action<double>? Dragged;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double bh = double.IsNaN(BarHeight) ? h : Math.Min(h, BarHeight), y = (h - bh) / 2, r = bh / 2;

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // toda el área responde al ratón
        dc.DrawRoundedRectangle(Track, null, new Rect(0, y, w, bh), r, r);
        double v = double.IsNaN(Value) ? 0 : Math.Clamp(Value, 0, 1);
        if (v > 0) dc.DrawRoundedRectangle(Fill, null, new Rect(0, y, Math.Max(bh, w * v), bh), r, r);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Dragged == null) return;
        CaptureMouse();
        Report(e);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e) { if (IsMouseCaptured) Report(e); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { if (IsMouseCaptured) ReleaseMouseCapture(); }

    void Report(MouseEventArgs e)
    {
        if (ActualWidth <= 0) return;
        double v = Math.Clamp(e.GetPosition(this).X / ActualWidth, 0, 1);
        Value = v;
        Dragged?.Invoke(v);
    }
}

/// <summary>Desplazamiento animado para la lista de letras.</summary>
public static class ScrollAnim
{
    public static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "Offset", typeof(double), typeof(ScrollAnim),
        new PropertyMetadata(0.0, (d, e) => ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));

    public static void To(ScrollViewer sv, double offset)
    {
        var a = new DoubleAnimation(sv.VerticalOffset, Math.Max(0, offset), TimeSpan.FromMilliseconds(380)) { EasingFunction = Ui.Smooth };
        sv.BeginAnimation(OffsetProperty, a);
    }
}
