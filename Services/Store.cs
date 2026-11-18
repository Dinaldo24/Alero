using System.Text.Json;

namespace Alero;

public class AppData
{
    public bool MinimalNotch { get; set; }
    /// <summary>El notch se queda abierto (menos en pantalla completa) en vez de cerrarse al quitar el ratón.</summary>
    public bool KeepOpen { get; set; } = false;
    /// <summary>Última duración elegida en el temporizador.</summary>
    public int TimerMinutes { get; set; } = 10;
    /// <summary>Última duración en segundos (si es 0 se usa TimerMinutes, de versiones anteriores).</summary>
    public int TimerSeconds { get; set; }
    /// <summary>Últimos tiempos iniciados (en segundos), el más reciente primero. Máximo 3.</summary>
    public List<int> TimerHistory { get; set; } = new() { 600, 300, 60 };
    /// <summary>Orden de las opciones del menú (nombres internos), elegido arrastrándolas.</summary>
    /// <summary>Paleta de color elegida (id).</summary>
    public string Theme { get; set; } = "amber";
    public List<string> TabOrder { get; set; } = new();
}

/// <summary>Ajustes en un único JSON dentro de %AppData%\Alero. Nada sale del equipo.</summary>
public static class Store
{
    public static AppData Data { get; private set; } = new();
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Alero");
    static string FilePath => Path.Combine(Dir, "data.json");
    static bool _volatile;

    public static void Load(bool volatileMode)
    {
        _volatile = volatileMode;
        if (volatileMode) return;
        try
        {
            if (File.Exists(FilePath))
                Data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(FilePath)) ?? new AppData();
        }
        catch { Data = new AppData(); }
    }

    public static void Save()
    {
        if (_volatile) return;
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
