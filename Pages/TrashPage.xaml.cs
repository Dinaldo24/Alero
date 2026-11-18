namespace Alero.Pages;

/// <summary>Arrastra archivos al recuadro y van a la Papelera de reciclaje.</summary>
public partial class TrashPage : UserControl
{
    const string IdleTitle = "Arrastra aquí para borrar", IdleSub = "Va a la Papelera: se puede recuperar";
    bool _highlighted;
    string? _status;

    public TrashPage()
    {
        InitializeComponent();

        DropZone.DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            // Se responde "copiar" a propósito: así la aplicación de origen no toca el archivo
            // y es Alero quien lo envía a la Papelera (recuperable), nunca un borrado directo.
            e.Effects = e.AllowedEffects.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : DragDropEffects.Link;
            e.Handled = true;
            Highlight(true);
        };
        DropZone.DragLeave += (_, _) => Highlight(false);
        DropZone.Drop += (_, e) =>
        {
            e.Handled = true;
            Highlight(false);
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
            // Se borra después de terminar el arrastre, para no dejar esperando a la aplicación de origen.
            Dispatcher.BeginInvoke(() => NotchWindow.Instance.TrashFiles(files));
        };
    }

    /// <summary>Resalta el recuadro mientras hay archivos encima.</summary>
    public void Highlight(bool on)
    {
        if (_highlighted == on) return;
        _highlighted = on;
        DropZone.Background = on ? new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0x6B, 0x5E)) : new SolidColorBrush(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF));
        DashColor.Brush = on ? Ui.Brush("Danger") : new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
        ZoneIcon.Foreground = Ui.Brush(on ? "Danger" : "Mist");
        ZoneTitle.Text = on ? "Suelta para borrar" : _status ?? IdleTitle;
        ZoneSub.Text = on ? "Se enviará a la Papelera" : IdleSub;
    }

    public void SetStatus(string text)
    {
        _status = text;
        if (!_highlighted) ZoneTitle.Text = text;
    }

    public void Refresh()
    {
        var (items, bytes) = Trash.Query();
        CountText.Text = items == 0 ? "Vacía" : items == 1 ? "1 elemento" : $"{items:N0} elementos";
        SizeText.Text = items == 0 ? "No hay nada que vaciar" : Ui.Size(bytes);
        EmptyBtn.IsEnabled = items > 0;
    }

    void Empty_Click(object sender, RoutedEventArgs e)
    {
        Trash.Empty(); // Windows pide confirmación antes de eliminar para siempre
        _status = null;
        ZoneTitle.Text = IdleTitle;
        Refresh();
    }

    void Open_Click(object sender, RoutedEventArgs e) => Trash.OpenFolder();
}
