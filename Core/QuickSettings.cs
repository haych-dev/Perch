using System.Diagnostics;
using System.Management;
using Microsoft.Win32;
using Windows.Devices.Radios;

namespace Perch.Core;

/// <summary>Wi-Fi and Bluetooth radios, dark mode, display brightness, and start-up registration.</summary>
public static class QuickSettings
{
    // ───────────── Radios ─────────────
    public static async Task<Radio?> GetRadioAsync(RadioKind kind)
    {
        try
        {
            var access = await Radio.RequestAccessAsync();
            if (access != RadioAccessStatus.Allowed) return null;
            var radios = await Radio.GetRadiosAsync();
            return radios.FirstOrDefault(r => r.Kind == kind);
        }
        catch { return null; }
    }

    public static async Task<bool> SetRadioAsync(Radio radio, bool on)
    {
        try { return await radio.SetStateAsync(on ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed; }
        catch { return false; }
    }

    // ───────────── Dark mode ─────────────
    const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsDarkMode()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Personalize);
        return (k?.GetValue("AppsUseLightTheme") as int? ?? 1) == 0;
    }

    public static void SetDarkMode(bool dark)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(Personalize))
        {
            k.SetValue("AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
            k.SetValue("SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        }
        Native.SendMessageTimeout(Native.HWND_BROADCAST, Native.WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet", Native.SMTO_ABORTIFHUNG, 200, out _);
    }

    // ───────────── Brightness (built-in displays only) ─────────────
    public static int GetBrightness()
    {
        try
        {
            using var q = new ManagementObjectSearcher(new ManagementScope(@"root\wmi"), new SelectQuery("WmiMonitorBrightness"));
            foreach (ManagementObject o in q.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch { }
        return -1;
    }

    public static void SetBrightness(int pct)
    {
        try
        {
            using var q = new ManagementObjectSearcher(new ManagementScope(@"root\wmi"), new SelectQuery("WmiMonitorBrightnessMethods"));
            foreach (ManagementObject o in q.Get()) o.InvokeMethod("WmiSetBrightness", [1u, (byte)Math.Clamp(pct, 0, 100)]);
        }
        catch { }
    }

    // ───────────── Start with Windows ─────────────
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void SetStartup(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) k.SetValue("Perch", $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue("Perch", false);
        }
        catch { }
    }

    public static bool IsStartupEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue("Perch") != null;
    }

    public static void OpenSettings(string page)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:" + page) { UseShellExecute = true }); } catch { }
    }

    public static void ToggleDesktop()
    {
        try
        {
            var t = Type.GetTypeFromProgID("Shell.Application");
            if (t == null) return;
            dynamic shell = Activator.CreateInstance(t)!;
            shell.ToggleDesktop();
        }
        catch { Native.PressKeys(Native.VK_LWIN, Native.VK_D); }
    }
}
