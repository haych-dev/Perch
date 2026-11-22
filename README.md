# Perch

A dock for Windows 10 and 11 that replaces the taskbar: your pinned and open apps sit next to live widgets on one bar at the bottom or top of the screen. Built with C# and WPF on .NET 10, with one NuGet dependency (System.Management, for display brightness).

## What it does

**The dock**

- Pinned apps and open windows, grouped by app the way the taskbar groups them. The first start copies your taskbar pins.
- Click an app to bring it forward, or click again to minimise it. Middle-click or Shift-click opens a new window. Rest the pointer on an app to see live thumbnails of its windows.
- Right-click an app to switch to one of its windows, pin or unpin it, open its file location, close its windows, or end the task.
- Icons magnify as the pointer passes over them, and names show on hover.
- Drag tiles to reorder them. Drop files or shortcuts on the dock to pin them, drop files on an app to open them with it, or drop files on the Recycle Bin.
- Hides the Windows taskbar while it runs and puts it back when Perch quits. The Windows key still opens Start.
- Reserves its own strip of the screen so maximised windows stop above it, or auto-hides and slides in when the pointer touches the screen edge.
- Steps aside for full-screen games and videos.

**Widgets** (right-click the dock and choose *Widgets…*)

| Group | Widgets |
| --- | --- |
| Essentials | Start, Search (apps, paths, websites, web search), Quick settings (Wi-Fi, Bluetooth, dark mode, brightness), Weather, Show desktop, Screenshot, Divider |
| Time | Clock with calendar and world clocks, Date, Day progress |
| System | CPU with busiest apps, Memory, Network speed, Storage, Battery |
| Media | Now playing (any app that reports to Windows' media controls), Sound (volume, mute, microphone) |
| Productivity | Focus timer (focus sessions with breaks, countdown, stopwatch), To-dos, Notes |
| Files | Downloads, Recycle Bin |

**Settings**: five styles (Midnight glass, Light glass, Black, White, Clear), accent colour, icon size, magnification, corner radius, spacing, padding, distance from the edge, full width, top or bottom edge, which display, auto-hide, lock, 12- or 24-hour clock, web search engine, temperature units, and start with Windows.

The glass styles blur the wallpaper behind the dock. Windows only draws the blur when **Settings › Personalisation › Colours › Transparency effects** is on; with it off, the glass styles show a solid tint.

## Privacy

- Nothing is sent anywhere except the weather: the place you choose goes to [Open-Meteo](https://open-meteo.com) to fetch the forecast. Open-Meteo needs no account or key.
- Settings, pins, to-dos and notes are kept in `%AppData%\Perch\settings.json`.

## If the taskbar doesn't come back

Perch saves the taskbar's original state before hiding it and restores it when it quits, when Windows signs out, and on its next start if it crashed. To restore it by hand, run:

```powershell
Perch.exe --restore-taskbar
```

## Build and run

Requires the .NET 10 SDK.

```powershell
dotnet build -c Release
```

```powershell
.\bin\Release\net10.0-windows10.0.19041.0\win-x64\Perch.exe
```

To quit, right-click an empty part of the dock and choose **Quit Perch**.

## Not done yet

- One dock, on one display at a time.
- No system tray (notification area) widget; while the taskbar is hidden, tray icons are only reachable by switching *Hide the Windows taskbar* off.
- No installer or code signing.
