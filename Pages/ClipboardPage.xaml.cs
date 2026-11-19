namespace Alero.Pages;

/// <summary>Historial del portapapeles: imágenes y texto (hasta 10 de cada uno), que se recorren deslizando.</summary>
public partial class ClipboardPage : UserControl
{
    static ClipboardService C => NotchWindow.Instance.Clips;

    bool _images = true;
    bool _active;

    public ClipboardPage()
    {
        InitializeComponent();
        Loaded += (_, _) => { C.Changed -= OnChanged; C.Changed += OnChanged; };

        // La rueda del ratón recorre la tira de imágenes de lado.
        ImgScroll.PreviewMouseWheel += (_, e) => { ImgScroll.ScrollToHorizontalOffset(ImgScroll.HorizontalOffset - e.Delta); e.Handled = true; };

    }

    // ---- Arrastrar una imagen hacia otra aplicación -------------------------------------------

    ClipImage? _pressed;
    Point _pressAt;

    /// <summary>Saca la imagen del notch: se puede soltar en un chat, un correo, Paint o una carpeta.</summary>
    void DragOut(UIElement source, ClipImage img)
    {
        var data = new DataObject();
        try { data.SetData(DataFormats.FileDrop, new[] { C.ExportToTempFile(img) }); } catch { }   // chats, correo, carpetas
        try { data.SetImage(C.Decode(img)); } catch { }                                           // Paint, Word, editores
        var host = NotchWindow.Instance;
        host.Dragging = true; host.DraggedClip = img;
        try { DragDrop.DoDragDrop(source, data, DragDropEffects.Copy); }
        catch { }
        finally { host.Dragging = false; host.DraggedClip = null; host.ScheduleClose(); }
    }

    void OnChanged() { if (_active) Refresh(); else UpdateCounts(); }

    public void SetActive(bool active)
    {
        _active = active;
        if (active) Refresh();
    }

    void UpdateCounts()
    {
        ModeImages.Content = $"Imágenes · {C.Images.Count}";
        ModeText.Content = $"Texto · {C.Texts.Count}";
    }

    public void ShowText() { _images = false; Refresh(); }

    public void Refresh()
    {
        UpdateCounts();
        ModeImages.Background = _images ? Ui.Brush("Card") : Brushes.Transparent;
        ModeImages.Foreground = Ui.Brush(_images ? "Accent" : "Mist");
        ModeText.Background = _images ? Brushes.Transparent : Ui.Brush("Card");
        ModeText.Foreground = Ui.Brush(_images ? "Mist" : "Accent");
        ImgScroll.Visibility = _images ? Visibility.Visible : Visibility.Collapsed;
        TextScroll.Visibility = _images ? Visibility.Collapsed : Visibility.Visible;

        int count = _images ? C.Images.Count : C.Texts.Count;
        ClearBtn.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _images
            ? "Las capturas y las imágenes que copies aparecerán aquí (hasta 10)"
            : "Los textos que copies aparecerán aquí (hasta 10)";

        if (_images) BuildImages(); else BuildTexts();
    }

    // ---- Imágenes -----------------------------------------------------------------------------

    void BuildImages()
    {
        ImgList.Children.Clear();
        foreach (var img in C.Images)
        {
            var card = new Grid { Width = 132, Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(82) });
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var thumb = new Border
            {
                CornerRadius = new CornerRadius(12), Background = new ImageBrush(img.Thumb) { Stretch = Stretch.UniformToFill },
                BorderBrush = Ui.Brush("Track"), BorderThickness = new Thickness(1),
            };
            card.Children.Add(thumb);

            var caption = new TextBlock
            {
                Text = $"{Ui.Ago(img.When)} · {img.Width}×{img.Height}", FontSize = 10.5, Foreground = Ui.Brush("Mist"),
                Margin = new Thickness(2, 5, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetRow(caption, 1);
            card.Children.Add(caption);

            var remove = RemoveButton();
            remove.HorizontalAlignment = HorizontalAlignment.Right; remove.VerticalAlignment = VerticalAlignment.Top;
            remove.Margin = new Thickness(0, 5, 5, 0);
            remove.MouseLeftButtonUp += (_, e) => { C.Remove(img); e.Handled = true; };
            card.Children.Add(remove);

            card.MouseEnter += (_, _) => remove.Opacity = 1;
            card.MouseLeave += (_, _) => remove.Opacity = 0;
            card.PreviewMouseLeftButtonDown += (_, e) => { _pressed = img; _pressAt = e.GetPosition(this); };
            card.MouseMove += (_, e) =>
            {
                if (_pressed != img || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(this);
                if (Math.Abs(p.X - _pressAt.X) < 6 && Math.Abs(p.Y - _pressAt.Y) < 6) return;
                _pressed = null;            // empezó un arrastre: ya no cuenta como clic
                caption.Text = "Suelta o arrastra a Borrar";
                caption.Foreground = Ui.Brush("Accent");
                DragOut(card, img);
                caption.Text = $"{Ui.Ago(img.When)} · {img.Width}×{img.Height}";
                caption.Foreground = Ui.Brush("Mist");
            };
            card.MouseLeftButtonUp += (_, _) =>
            {
                if (_pressed != img) return; // venía de un arrastre
                _pressed = null;
                C.CopyImage(img);
                caption.Text = "Copiada";
                caption.Foreground = Ui.Brush("Accent");
            };
            ImgList.Children.Add(card);
        }
    }

    static Border RemoveButton() => new()
    {
        Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)),
        Opacity = 0, Cursor = Cursors.Hand,
        Child = new TextBlock
        {
            Text = "", FontFamily = Ui.IconFont, FontSize = 9, Foreground = Ui.Brush("Ink"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    // ---- Texto --------------------------------------------------------------------------------

    void BuildTexts()
    {
        TextList.Children.Clear();
        foreach (var clip in C.Texts)
        {
            var row = new Grid { Height = 29, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string oneLine = (clip.Text.Length > 200 ? clip.Text[..200] : clip.Text).ReplaceLineEndings(" ").Trim();
            var text = new TextBlock
            {
                Text = oneLine, FontSize = 12.5, Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var when = new TextBlock { Text = Ui.Ago(clip.When), FontSize = 10.5, Foreground = Ui.Brush("Mist"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(when, 1);

            var remove = RemoveButton();
            remove.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(remove, 2);
            remove.MouseLeftButtonUp += (_, e) => { C.Remove(clip); e.Handled = true; };

            row.Children.Add(text); row.Children.Add(when); row.Children.Add(remove);
            row.MouseEnter += (_, _) => remove.Opacity = 1;
            row.MouseLeave += (_, _) => remove.Opacity = 0;
            row.MouseLeftButtonUp += (_, _) =>
            {
                C.CopyText(clip);
                when.Text = "copiado";
                when.Foreground = Ui.Brush("Accent");
            };
            TextList.Children.Add(row);
        }
    }

    void ModeImages_Click(object sender, RoutedEventArgs e) { _images = true; Refresh(); }
    void ModeText_Click(object sender, RoutedEventArgs e) { _images = false; Refresh(); }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_images) C.ClearImages(); else C.ClearTexts();
    }
}
