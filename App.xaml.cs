using System.IO;
using System.Windows;
using System.Windows.Threading;
using Perch.Core;
using Perch.Dock;

namespace Perch;

public partial class App : Application
{
    static Mutex? _single;
    DockWindow? _dock;

    protected override void OnStartup(StartupEventArgs e)
    {
        // "Perch.exe --restore-taskbar" puts the Windows taskbar back without starting the dock.
        if (e.Args.Contains("--restore-taskbar", StringComparer.OrdinalIgnoreCase))
        {
            Settings.Current.TaskbarOriginalState = Math.Max(Settings.Current.TaskbarOriginalState, 0);
            Taskbar.Restore();
            Shutdown();
            return;
        }

        _single = new Mutex(true, "Perch.Dock.SingleInstance", out bool first);
        if (!first)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        bool firstRun = !File.Exists(Path.Combine(Settings.Folder, "settings.json"));
        DispatcherUnhandledException += (_, ex) => { Log(ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => { if (ex.ExceptionObject is Exception x) Log(x); Taskbar.Restore(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Taskbar.Restore();
        SessionEnding += (_, _) => Taskbar.Restore();

        Taskbar.RepairAfterCrash();
        Theme.Apply();
        Apps.ImportTaskbarPins();
        if (Settings.Current.StartWithWindows != QuickSettings.IsStartupEnabled()) Settings.Current.StartWithWindows = QuickSettings.IsStartupEnabled();

        SystemSampler.Instance.Start(Dispatcher);
        WindowTracker.Instance.Start();
        Media.Instance.Start(Dispatcher);
        Weather.Instance.Start();

        _dock = new DockWindow();
        _dock.Start();

        // First run: show where things are.
        if (firstRun)
            Dispatcher.BeginInvoke(() => SettingsWindow.ShowSingle(), DispatcherPriority.ApplicationIdle);
        Settings.SaveNow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _dock?.Shutdown(); } catch { }
        Taskbar.Restore();
        Settings.SaveNow();
        base.OnExit(e);
    }

    public static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Settings.Folder);
            File.AppendAllText(Path.Combine(Settings.Folder, "errors.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
    }
}
