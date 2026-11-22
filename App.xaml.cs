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
            // Shows the taskbar again; its auto-hide setting is only changed back if Perch saved the original.
            Taskbar.Restore();
            Shutdown();
            return;
        }

        // "Perch.exe --quit" asks a running Perch to quit the normal way (the installer and uninstaller use it).
        if (e.Args.Contains("--quit", StringComparer.OrdinalIgnoreCase))
        {
            QuitRunningInstance();
            Shutdown();
            return;
        }

        _single = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Perch is already running: leave it, its taskbar and its settings alone.
            Shutdown();
            return;
        }
        _primary = true;
        ListenForQuit();

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

    /// <summary>True only in the copy of Perch that is running the dock.</summary>
    static bool _primary;

    protected override void OnExit(ExitEventArgs e)
    {
        // A second copy that started and exited at once (opened again from Start, or "--quit") must not
        // restore the taskbar or save its own copy of the settings: that would undo the running dock's
        // auto-hide (the taskbar then reserves space and the dock moves up) and could overwrite newer settings.
        if (_primary)
        {
            try { _dock?.Shutdown(); } catch { }
            Taskbar.Restore();
            Settings.SaveNow();
        }
        base.OnExit(e);
    }

    const string MutexName = "Perch.Dock.SingleInstance";
    const string QuitEventName = "Perch.Dock.Quit";

    void ListenForQuit()
    {
        var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        var t = new Thread(() =>
        {
            quit.WaitOne();
            Dispatcher.BeginInvoke(Shutdown);
        }) { IsBackground = true, Name = "Perch.QuitListener" };
        t.Start();
    }

    static void QuitRunningInstance()
    {
        if (!EventWaitHandle.TryOpenExisting(QuitEventName, out var quit)) return;
        quit.Set();
        // Wait (up to 10 s) for it to put the taskbar back and exit.
        for (int i = 0; i < 100 && Mutex.TryOpenExisting(MutexName, out var m); i++)
        {
            m.Dispose();
            Thread.Sleep(100);
        }
    }

    public static void Log(Exception ex) => Perch.Core.Log.Error(ex);
}
