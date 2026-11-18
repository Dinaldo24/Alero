using Microsoft.Win32;

namespace Alero;

/// <summary>"Iniciar con Windows" para el usuario actual (no necesita permisos de administrador).</summary>
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "Alero";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                return k?.GetValue(Name) is string;
            }
            catch { return false; }
        }
    }

    public static void Set(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (k == null) return;
            if (on && Environment.ProcessPath is { } exe) k.SetValue(Name, $"\"{exe}\"");
            else k.DeleteValue(Name, throwOnMissingValue: false);
        }
        catch { }
    }
}
