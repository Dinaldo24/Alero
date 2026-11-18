using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Alero;

/// <summary>
/// "Borrar" envía a la Papelera de reciclaje de Windows: se puede recuperar desde ahí.
/// Si un archivo no cabe en la Papelera, Windows avisa antes de eliminarlo para siempre.
/// </summary>
public static class Trash
{
    const uint FO_DELETE = 3;
    const ushort FOF_SILENT = 0x0004, FOF_NOCONFIRMATION = 0x0010, FOF_ALLOWUNDO = 0x0040, FOF_WANTNUKEWARNING = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBinW(string? root, ref SHQUERYRBINFO info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? root, uint flags);

    /// <summary>Envía archivos y carpetas a la Papelera. Devuelve cuántos dejaron de existir en su sitio.</summary>
    public static int Recycle(IReadOnlyList<string> paths, IntPtr owner)
    {
        var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (existing.Count == 0) return 0;

        // Lista de rutas separadas por un carácter nulo y terminada con dos.
        IntPtr from = Marshal.StringToHGlobalUni(string.Join('\0', existing) + '\0');
        try
        {
            var op = new SHFILEOPSTRUCT
            {
                hwnd = owner, wFunc = FO_DELETE, pFrom = from,
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_WANTNUKEWARNING),
            };
            SHFileOperationW(ref op);
        }
        finally { Marshal.FreeHGlobal(from); }

        return existing.Count(p => !File.Exists(p) && !Directory.Exists(p));
    }

    /// <summary>Cuántos elementos hay en la Papelera y cuánto ocupan.</summary>
    public static (long items, long bytes) Query()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBinW(null, ref info) == 0 ? (info.i64NumItems, info.i64Size) : (0, 0);
    }

    /// <summary>Vacía la Papelera. Windows muestra su propia confirmación antes de eliminar para siempre.</summary>
    public static void Empty() => SHEmptyRecycleBinW(IntPtr.Zero, null, 0);

    public static void OpenFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true }); } catch { }
    }
}
