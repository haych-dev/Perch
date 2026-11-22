using System.Runtime.InteropServices;

namespace Perch.Core;

/// <summary>Master volume and mute for the default speakers and microphone (Core Audio).</summary>
public static class Audio
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
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
