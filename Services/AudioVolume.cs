using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Alero;

/// <summary>Volumen de una aplicación concreta dentro del mezclador de Windows.</summary>
public sealed class AppSession
{
    readonly Action<double> _set;
    public string Name { get; }
    public double Level { get; private set; }
    public bool Active { get; }

    internal AppSession(string name, double level, bool active, Action<double> set)
    {
        Name = name; Level = level; Active = active; _set = set;
    }

    public void Set(double level)
    {
        Level = Math.Clamp(level, 0, 1);
        try { _set(Level); } catch { }
    }
}

/// <summary>
/// Volumen general, volumen por aplicación y dispositivo de salida.
/// Windows avisa cuando cambia el volumen: no se escucha el teclado ni se sondea.
/// </summary>
public sealed class AudioVolume
{
    public event Action<double, bool>? Changed;

    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    IMMDeviceEnumerator? _enum;
    IMMDevice? _device;
    IAudioEndpointVolume? _vol;
    Callback? _cb;
    string? _deviceId;
    double _level = -1;
    bool _muted;

    public double Level => Math.Max(0, _level);
    public bool Muted => _muted;
    public string DeviceName { get; private set; } = "";
    public string? CurrentOutputId => _deviceId;

    public void Start() => EnsureDevice();

    /// <summary>Se llama cada pocos segundos por si cambió la salida (por ejemplo, al conectar auriculares).</summary>
    public void EnsureDevice()
    {
        try
        {
            _enum ??= (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (_enum.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out var dev) != 0 || dev == null) return;
            if (dev.GetId(out var id) != 0) return;
            if (id == _deviceId && _vol != null) return;

            if (_vol != null && _cb != null) { try { _vol.UnregisterControlChangeNotify(_cb); } catch { } }

            var iid = typeof(IAudioEndpointVolume).GUID;
            if (dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var obj) != 0) return;
            _vol = (IAudioEndpointVolume)obj;
            _device = dev;
            _deviceId = id;
            DeviceName = NameOf(dev);
            _vol.GetMasterVolumeLevelScalar(out var level);
            _vol.GetMute(out var muted);
            _level = level; _muted = muted;
            _cb = new Callback(this);
            _vol.RegisterControlChangeNotify(_cb);
        }
        catch { }
    }

    public void SetLevel(double level)
    {
        level = Math.Clamp(level, 0, 1);
        try
        {
            var ctx = Guid.Empty;
            _vol?.SetMasterVolumeLevelScalar((float)level, ref ctx);
            _level = level; // el aviso de Windows llegará con el mismo valor y no repetirá el evento
        }
        catch { }
    }

    public void SetMute(bool mute)
    {
        try
        {
            var ctx = Guid.Empty;
            _vol?.SetMute(mute, ref ctx);
            _muted = mute;
        }
        catch { }
    }

    void OnChange(double level, bool muted)
    {
        if (Math.Abs(level - _level) < 0.004 && muted == _muted) return;
        _level = level; _muted = muted;
        Changed?.Invoke(level, muted);
    }

    // ---- Volumen por aplicación ---------------------------------------------------------------

    public List<AppSession> GetSessions()
    {
        var list = new List<AppSession>();
        try
        {
            if (_device == null) return list;
            var iid = typeof(IAudioSessionManager2).GUID;
            if (_device.Activate(ref iid, 23, IntPtr.Zero, out var obj) != 0) return list;
            if (((IAudioSessionManager2)obj).GetSessionEnumerator(out var sessions) != 0 || sessions == null) return list;

            sessions.GetCount(out int count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var ctl) != 0 || ctl == null) continue;
                ctl.GetState(out int state);
                if (state == 2) continue; // la aplicación ya se cerró

                bool system = ctl.IsSystemSoundsSession() == 0;
                ctl.GetProcessId(out uint pid);
                string name = system ? "Sonidos del sistema" : AppName(pid);
                if (name.Length == 0 || !seen.Add(name)) continue;

                var simple = (ISimpleAudioVolume)ctl;
                simple.GetMasterVolume(out float level);
                list.Add(new AppSession(name, level, state == 1, v => { var ctx = Guid.Empty; simple.SetMasterVolume((float)v, ref ctx); }));
            }
        }
        catch { }
        return list.OrderByDescending(s => s.Active).ThenBy(s => s.Name).ToList();
    }

    static string AppName(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            try
            {
                if (p.MainModule?.FileVersionInfo.FileDescription is { Length: > 0 } description) return description;
            }
            catch { /* proceso protegido: basta con su nombre */ }
            string n = p.ProcessName;
            return n.Length > 0 ? char.ToUpper(n[0]) + n[1..] : "";
        }
        catch { return ""; }
    }

    // ---- Dispositivo de salida ----------------------------------------------------------------

    static string NameOf(IMMDevice dev)
    {
        try
        {
            if (dev.OpenPropertyStore(0 /* lectura */, out var store) != 0 || store == null) return "";
            var key = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 }; // nombre visible
            if (store.GetValue(ref key, out var value) != 0) return "";
            try { return value.vt == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.p) ?? "" : ""; }
            finally { PropVariantClear(ref value); }
        }
        catch { return ""; }
    }

    /// <summary>Salidas activas (altavoces, auriculares, monitor…), con su identificador.</summary>
    public List<(string id, string name)> GetOutputs()
    {
        var list = new List<(string, string)>();
        try
        {
            _enum ??= (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (_enum.EnumAudioEndpoints(0, 1 /* activos */, out var devices) != 0 || devices == null) return list;
            devices.GetCount(out int count);
            for (int i = 0; i < count; i++)
                if (devices.Item(i, out var d) == 0 && d != null && d.GetId(out var id) == 0) list.Add((id, NameOf(d)));
        }
        catch { }
        return list;
    }

    /// <summary>Hace que Windows use esa salida por defecto. Devuelve true si Windows lo aceptó.</summary>
    public bool SetOutput(string id)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            bool ok = policy.SetDefaultEndpoint(id, 0 /* consola */) == 0;
            ok &= policy.SetDefaultEndpoint(id, 1 /* multimedia */) == 0;
            EnsureDevice();
            return ok;
        }
        catch { return false; }
    }

    /// <summary>Pasa a la siguiente salida disponible. Devuelve su nombre, o null si solo hay una.</summary>
    public string? NextOutput()
    {
        var outputs = GetOutputs();
        if (outputs.Count < 2) return null;
        int i = outputs.FindIndex(o => o.id == _deviceId);
        var next = outputs[(i + 1) % outputs.Count];
        return SetOutput(next.id) ? next.name : null;
    }

    // ---- COM ----------------------------------------------------------------------------------

    sealed class Callback(AudioVolume owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr data)
        {
            // AUDIO_VOLUME_NOTIFICATION_DATA: GUID (16) · BOOL bMuted · float fMasterVolume · …
            bool muted = Marshal.ReadInt32(data, 16) != 0;
            float level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20));
            owner._ui.BeginInvoke(() => owner.OnChange(level, muted));
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPERTYKEY { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPVARIANT { public ushort vt, r1, r2, r3; public IntPtr p; public IntPtr p2; }

    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT value);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr data);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(ref Guid group, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr events);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr events);
        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    // Interfaz interna de Windows para elegir la salida por defecto (la misma que usa su panel de sonido).
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    class PolicyConfigClient { }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
    }
}
