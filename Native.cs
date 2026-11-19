using System.Runtime.InteropServices;
using System.Text;

namespace Alero;

/// <summary>Llamadas a Win32 que WPF no expone.</summary>
internal static class Native
{
    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    public const long WS_MAXIMIZE = 0x01000000;
    public const int WM_HOTKEY = 0x0312, WM_CLIPBOARDUPDATE = 0x031D, WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;
    public const int VK_SPACE = 0x20, VK_CAPITAL = 0x14, VK_T = 0x54;
    static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
    public static IntPtr Foreground() => GetForegroundWindow();
    public static void Focus(IntPtr hwnd) { if (hwnd != IntPtr.Zero && IsWindow(hwnd)) SetForegroundWindow(hwnd); }
    [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    public static bool TryGetCursor(out POINT p) => GetCursorPos(out p);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    public const byte VK_MEDIA_NEXT_TRACK = 0xB0, VK_MEDIA_PREV_TRACK = 0xB1;

    /// <summary>Pulsa una tecla multimedia, igual que las del teclado (la atiende el reproductor activo).</summary>
    public static void MediaKey(byte vk)
    {
        keybd_event(vk, 0, 1 /* EXTENDEDKEY */, UIntPtr.Zero);
        keybd_event(vk, 0, 1 | 2 /* KEYUP */, UIntPtr.Zero);
    }

    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
    public static void EnsureShown(IntPtr hwnd) => ShowWindow(hwnd, 4 /* SW_SHOWNOACTIVATE */);

    public static void SetExStyle(IntPtr hwnd, long flag, bool on)
    {
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex = on ? ex | flag : ex & ~flag;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    public static void KeepOnTop(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOSIZE | NOMOVE | NOACTIVATE

    /// <summary>True si la ventana activa ocupa toda la pantalla principal (juego, video, presentación).</summary>
    /// <summary>Hay una aplicación maximizada delante (no el escritorio ni la barra de tareas).</summary>
    public static bool IsForegroundMaximized(IntPtr self)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == self || fg == GetDesktopWindow() || fg == GetShellWindow()) return false;
        var sb = new StringBuilder(64);
        GetClassName(fg, sb, sb.Capacity);
        if (sb.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "XamlExplorerHostIslandWindow") return false;
        return (GetWindowLongPtr(fg, GWL_STYLE).ToInt64() & WS_MAXIMIZE) != 0;
    }

    public static bool IsForegroundFullscreen(IntPtr self)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == self || fg == GetDesktopWindow() || fg == GetShellWindow()) return false;

        var sb = new StringBuilder(64);
        GetClassName(fg, sb, sb.Capacity);
        if (sb.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "XamlExplorerHostIslandWindow") return false;

        // Una ventana maximizada no es pantalla completa aunque la barra de tareas se oculte sola.
        if ((GetWindowLongPtr(fg, GWL_STYLE).ToInt64() & WS_MAXIMIZE) != 0) return false;
        if (!GetWindowRect(fg, out var r)) return false;

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(fg, 2), ref mi)) return false;
        if ((mi.dwFlags & 1) == 0) return false; // no es el monitor principal

        return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top &&
               r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
    }
}
