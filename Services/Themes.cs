namespace Alero;

/// <summary>Paletas de color de acento. El fondo sigue siendo negro; solo cambia el color de resalte.</summary>
public static class Themes
{
    public record Palette(string Id, string Name, string Accent);

    public static readonly Palette[] All =
    {
        new("amber",  "Ámbar",    "#FFB454"),
        new("violet", "Violeta",  "#B994FF"),
        new("indigo", "Índigo",   "#8F9BFF"),
        new("blue",   "Azul",     "#5FB0FF"),
        new("cyan",   "Cian",     "#4FD9E8"),
        new("mint",   "Menta",    "#5EE6A8"),
        new("lime",   "Lima",     "#C5E866"),
        new("rose",   "Rosa",     "#FF8EC4"),
        new("coral",  "Coral",    "#FF7F6E"),
    };

    public static Palette Current => All.FirstOrDefault(p => p.Id == Store.Data.Theme) ?? All[0];

    public static void Apply(Palette p)
    {
        var c = (Color)ColorConverter.ConvertFromString(p.Accent);
        Set("Accent", c);
        // Texto sobre el acento: oscuro, con un toque del propio color
        Set("OnAccent", Color.FromRgb((byte)(c.R / 10), (byte)(c.G / 10), (byte)(c.B / 10)));
        Store.Data.Theme = p.Id;
    }

    static void Set(string key, Color c)
    {
        var res = Application.Current.Resources;
        if (res[key] is SolidColorBrush b && !b.IsFrozen) b.Color = c;
        else res[key] = new SolidColorBrush(c);
    }
}
