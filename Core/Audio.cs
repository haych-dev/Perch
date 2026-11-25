using System.Runtime.InteropServices;

namespace Perch.Core;

/// <summary>Master volume and mute for the default speakers and microphone (Core Audio).</summary>
public static class Audio
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    // Windows' own interface for changing the default device. It isn't documented, but Sound settings and many
    // audio switchers use it, and it has been stable since Windows 7.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
        [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
        [PreserveSig] int ResetDeviceFormat(IntPtr a);
        [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
        [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        [PreserveSig] int SetEndpointVisibility(IntPtr a, int b);
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] class PolicyConfigCom { }

    public readonly record struct Device(string Id, string Name, bool IsDefault);

    /// <summary>Active speaker/headphone outputs, the default one marked.</summary>
    public static List<Device> Outputs()
    {
        var list = new List<Device>();
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            string defId = "";
            if (en.GetDefaultAudioEndpoint(eRender, eMultimedia, out var def) == 0 && def != null) def.GetId(out defId);
            if (en.EnumAudioEndpoints(eRender, 1 /* DEVICE_STATE_ACTIVE */, out var col) != 0 || col == null) return list;
            col.GetCount(out uint n);
            for (uint i = 0; i < n; i++)
            {
                if (col.Item(i, out var dev) != 0 || dev == null) continue;
                dev.GetId(out string id);
                list.Add(new Device(id, FriendlyName(dev), id == defId));
            }
        }
        catch (Exception ex) { Log.Error(ex, "audio outputs could not be listed"); }
        return list;
    }

    /// <summary>Makes a device the default for everything (media, communications and system sounds).</summary>
    public static bool SetDefaultOutput(string id)
    {
        try
        {
            var pc = (IPolicyConfig)new PolicyConfigCom();
            bool ok = true;
            for (int role = 0; role < 3; role++) ok &= pc.SetDefaultEndpoint(id, role) == 0;
            return ok;
        }
        catch (Exception ex) { Log.Error(ex, "the default audio output could not be changed"); return false; }
    }

    static string FriendlyName(IMMDevice dev)
    {
        if (dev.OpenPropertyStore(0, out var store) != 0 || store == null) return "Audio device";
        var key = PKEY_Device_FriendlyName;
        if (store.GetValue(ref key, out var pv) != 0) return "Audio device";
        try { return pv.vt == 31 && pv.p != IntPtr.Zero ? Marshal.PtrToStringUni(pv.p) ?? "Audio device" : "Audio device"; }
        finally { Native.PropVariantClear(ref pv); }
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out Native.IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    static readonly Native.PROPERTYKEY PKEY_Device_FriendlyName = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    const int eRender = 0, eCapture = 1, eMultimedia = 1;

    static IAudioEndpointVolume? Endpoint(int flow, out string name)
    {
        name = "";
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (en.GetDefaultAudioEndpoint(flow, eMultimedia, out var dev) != 0 || dev == null) return null;
            if (dev.OpenPropertyStore(0, out var store) == 0 && store != null)
            {
                var key = PKEY_Device_FriendlyName;
                if (store.GetValue(ref key, out var pv) == 0)
                {
                    if (pv.vt == 31 && pv.p != IntPtr.Zero) name = Marshal.PtrToStringUni(pv.p) ?? "";
                    Native.PropVariantClear(ref pv);
                }
            }
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (dev.Activate(ref iid, 23, IntPtr.Zero, out var o) != 0) return null;
            return o as IAudioEndpointVolume;
        }
        catch { return null; }
    }

    public readonly record struct State(bool Available, float Level, bool Muted, string Device);

    public static State Speakers() => Read(eRender);
    public static State Microphone() => Read(eCapture);

    static State Read(int flow)
    {
        var ep = Endpoint(flow, out var name);
        if (ep == null) return new State(false, 0, false, "");
        ep.GetMasterVolumeLevelScalar(out float level);
        ep.GetMute(out bool muted);
        return new State(true, level, muted, name);
    }

    public static void SetVolume(float level)
    {
        var ep = Endpoint(eRender, out _);
        if (ep == null) return;
        var g = Guid.Empty;
        ep.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref g);
        if (level > 0) ep.SetMute(false, ref g);
    }

    public static void SetMute(bool mute, bool microphone = false)
    {
        var ep = Endpoint(microphone ? eCapture : eRender, out _);
        if (ep == null) return;
        var g = Guid.Empty;
        ep.SetMute(mute, ref g);
    }
}
