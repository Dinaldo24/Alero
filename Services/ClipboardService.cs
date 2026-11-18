using System.Security.Cryptography;
using System.Windows.Media.Imaging;

namespace Alero;

public sealed record ClipText(string Text, DateTime When);

public sealed class ClipImage
{
    public required byte[] Png { get; init; }
    public required ImageSource Thumb { get; init; }
    public required string Hash { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public DateTime When { get; init; }
}

/// <summary>
/// Historial del portapapeles: las últimas 10 imágenes (capturas de pantalla incluidas) y los últimos 10 textos.
/// Vive solo en memoria: se borra al cerrar Alero. Lo que un gestor de contraseñas marca como sensible se ignora.
/// </summary>
public sealed class ClipboardService
{
    public const int Max = 10;
    const string ExcludeFormat = "ExcludeClipboardContentFromMonitorProcessing";

    public List<ClipText> Texts { get; } = new();
    public List<ClipImage> Images { get; } = new();
    public event Action? Changed;

    DateTime _ignoreUntil;
    int _generation;

    /// <summary>Windows avisa de un cambio. El programa que copió puede tener el portapapeles ocupado: se reintenta unos instantes.</summary>
    public async void OnClipboardUpdate()
    {
        if (DateTime.Now < _ignoreUntil) return;
        int gen = ++_generation;
        for (int i = 0; i < 5; i++)
        {
            if (gen != _generation) return; // llegó una copia más reciente
            try { if (await TryCaptureNow()) return; } catch { }
            await Task.Delay(100 * (i + 1));
        }
    }

    /// <summary>Lee el portapapeles. Devuelve false si estaba ocupado y conviene reintentar.</summary>
    public async Task<bool> TryCaptureNow()
    {
        try
        {
            if (Clipboard.ContainsData(ExcludeFormat)) return true;
            // Si hay texto, es texto (Excel, por ejemplo, copia también una imagen de las celdas). Una captura solo trae imagen.
            if (Clipboard.ContainsText()) { AddText(Clipboard.GetText()); return true; }
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } img) await AddImage(img);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException) { return false; }
    }

    public void AddText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 20000) return;
        Texts.RemoveAll(t => t.Text == text);
        Texts.Insert(0, new ClipText(text, DateTime.Now));
        if (Texts.Count > Max) Texts.RemoveRange(Max, Texts.Count - Max);
        Changed?.Invoke();
    }

    public async Task AddImage(BitmapSource source)
    {
        // Guardar la imagen sin comprimir ocuparía decenas de MB por captura: se guarda como PNG.
        ClipImage? item;
        if (source.CanFreeze) { source.Freeze(); item = await Task.Run(() => Build(source)); }
        else item = Build(source);
        if (item == null) return;

        Images.RemoveAll(i => i.Hash == item.Hash);
        Images.Insert(0, item);
        if (Images.Count > Max) Images.RemoveRange(Max, Images.Count - Max);
        Changed?.Invoke();
    }

    static ClipImage? Build(BitmapSource source)
    {
        try
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(source));
            using var ms = new MemoryStream();
            enc.Save(ms);
            byte[] png = ms.ToArray();

            var thumb = new BitmapImage();
            thumb.BeginInit();
            thumb.CacheOption = BitmapCacheOption.OnLoad;
            thumb.DecodePixelWidth = 260;
            thumb.StreamSource = new MemoryStream(png);
            thumb.EndInit();
            thumb.Freeze();

            return new ClipImage
            {
                Png = png, Thumb = thumb, Hash = Convert.ToHexString(SHA256.HashData(png)),
                Width = source.PixelWidth, Height = source.PixelHeight, When = DateTime.Now,
            };
        }
        catch { return null; }
    }

    // ---- Volver a copiar ----------------------------------------------------------------------

    public void CopyText(ClipText item)
    {
        try { _ignoreUntil = DateTime.Now.AddMilliseconds(800); Clipboard.SetText(item.Text); } catch { }
    }

    public BitmapSource Decode(ClipImage item)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = new MemoryStream(item.Png);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    public void CopyImage(ClipImage item)
    {
        try
        {
            var bmp = Decode(item);
            _ignoreUntil = DateTime.Now.AddMilliseconds(800);
            Clipboard.SetImage(bmp);
        }
        catch { }
    }

    static string TempDir => Path.Combine(Path.GetTempPath(), "Alero");

    /// <summary>Guarda la imagen como PNG en una carpeta temporal, para poder arrastrarla como archivo.</summary>
    public string ExportToTempFile(ClipImage item)
    {
        Directory.CreateDirectory(TempDir);
        string path = Path.Combine(TempDir, $"captura-{item.When:yyyyMMdd-HHmmss}-{item.Hash[..6]}.png");
        if (!File.Exists(path)) File.WriteAllBytes(path, item.Png);
        return path;
    }

    /// <summary>Quita las imágenes temporales de arrastres anteriores.</summary>
    public static void CleanTemp()
    {
        try
        {
            if (!Directory.Exists(TempDir)) return;
            foreach (var f in Directory.GetFiles(TempDir, "captura-*.png"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddHours(-12)) File.Delete(f);
        }
        catch { }
    }

    public void Remove(ClipText item) { Texts.Remove(item); Changed?.Invoke(); }
    public void Remove(ClipImage item) { Images.Remove(item); Changed?.Invoke(); }
    public void ClearTexts() { Texts.Clear(); Changed?.Invoke(); }
    public void ClearImages() { Images.Clear(); Changed?.Invoke(); }

    /// <summary>Datos de muestra para las capturas de verificación.</summary>
    public async Task AddDemo()
    {
        string[] texts =
        {
            "Reunión movida a las 3 PM, sala B", "https://ejemplo.com/descarga/archivo.zip", "Pedido #48213 enviado",
            "Kopi susu 2, roti bakar 1", "correo@ejemplo.com", "Llamar a Diana mañana",
        };
        foreach (var t in texts.Reverse()) AddText(t);

        (Color a, Color b, int w, int h)[] shots =
        {
            (Color.FromRgb(0x2B, 0x59, 0xC3), Color.FromRgb(0x8E, 0xC5, 0xFC), 1920, 1080),
            (Color.FromRgb(0xC2, 0x41, 0x0C), Color.FromRgb(0xFD, 0xBA, 0x74), 1280, 720),
            (Color.FromRgb(0x16, 0x65, 0x34), Color.FromRgb(0x86, 0xEF, 0xAC), 2560, 1440),
            (Color.FromRgb(0x6B, 0x21, 0xA8), Color.FromRgb(0xD8, 0xB4, 0xFE), 800, 600),
        };
        foreach (var s in shots)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(s.a, s.b, 45), null, new Rect(0, 0, s.w, s.h));
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), null, new Rect(s.w * .1, s.h * .15, s.w * .5, s.h * .5), 24, 24);
            }
            var bmp = new RenderTargetBitmap(s.w, s.h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            await AddImage(bmp);
        }
    }
}
