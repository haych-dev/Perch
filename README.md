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
| Essentials | Start, Quick launch (your pinned apps), System tray (brings up the Windows tray), Wallpapers, Search (apps, paths, websites, web search), Quick settings (Wi-Fi, Bluetooth, dark mode, brightness), Weather, Show desktop, Screenshot, Divider |
| Time | Clock with calendar and world clocks, Date, Day progress, Analog clock, Flip clock, Segment clock, World clocks, Day number, Day timeline |
| Weather | Next hours, Next days, Sunrise & sunset |
| System | CPU with busiest apps, Memory, Network speed, Storage, Battery, Battery ring, GPU (load, plus temperature and video memory on NVIDIA), Top CPU, Top memory, Brightness (laptop screens and DDC/CI monitors), Light / dark, Notifications, Keyboard language |
| Media | Now playing (any app that reports to Windows' media controls), Now playing (small), Sound (volume, mute, microphone), Microphone, Sound output (switch speakers and headphones) |
| Productivity | Focus timer (focus sessions with breaks, countdown, stopwatch), To-dos, Notes, Commands (your own one-click commands) |
| Files | Downloads, Recent files, Recycle Bin |
| Online | Currency (160 currencies), Crypto (Binance prices), GitHub activity, LeetCode |
| AI | Claude Code (this 5-hour window, the last 7 days, most-used model and sessions working now) |

**Quick launch** holds your pinned apps in one tile. Add apps from the same list Start shows (Store apps included; help files, uninstallers and admin tools left out) or by dragging them onto the tile. While Quick launch is on the dock, the apps group shows only open apps, so the dock stays short however many apps you pin.

**Wallpapers** is a gallery of 15 original designs that Perch draws itself at your screen's resolution: mesh gradients, auroras, dunes, mountain ridges, petals, glass orbs, flowing lines, and one in your accent colour. Nothing is downloaded. Click one to set it as your wallpaper and lock screen (the lock screen can be switched off), use *Surprise me* for a fresh layout, or switch on a new wallpaper every day. Images are saved in `Pictures\Perch Wallpapers` (the newest few are kept), and *Restore my old wallpaper* puts back the wallpaper and lock screen you had before Perch first changed them. `Perch.exe --export-wallpapers <folder>` saves the whole gallery as PNG files.

**Settings**: five styles (Midnight glass, Light glass, Black, White, Clear), accent colour, icon size, magnification, corner radius, spacing, padding, distance from the edge, full width, top or bottom edge, which display, auto-hide, lock, 12- or 24-hour clock, web search engine, temperature units, and start with Windows.

The glass styles blur the wallpaper behind the dock. Windows only draws the blur when **Settings › Personalisation › Colours › Transparency effects** is on; with it off, the glass styles show a solid tint.

## Privacy

- Nothing is sent anywhere except what the widgets you add need, and none of them needs an account or key:
  - Weather: the place you choose goes to [Open-Meteo](https://open-meteo.com).
  - Currency: the currency code goes to [ExchangeRate-API](https://www.exchangerate-api.com)'s free rates.
  - Crypto: the coin symbols go to Binance's public price API.
  - GitHub activity and LeetCode: the user name you enter goes to GitHub or LeetCode, which return only public profile data.
  - Claude Code reads Claude Code's own history files on your PC (`%UserProfile%\.claude\projects`). It doesn't read your Claude sign-in and sends nothing.
- Settings, pins, to-dos and notes are kept in `%AppData%\Perch\settings.json`, with a copy of the last settings that loaded successfully in `settings.backup.json`.

## If the taskbar doesn't come back

Perch saves the taskbar's original state before hiding it and restores it when it quits, when Windows signs out, and on its next start if it crashed. To restore it by hand, run:

```powershell
Perch.exe --restore-taskbar
```

The installer also adds a **Perch - restore the taskbar** shortcut to the Start menu that does the same. `Perch.exe --quit` closes a running Perch the normal way.

## Install

`Perch-<version>-win-x64-setup.exe` installs Perch for your account (no administrator prompt) with a Start menu entry, and by default starts it when you sign in. It carries its own .NET runtime, so nothing else is needed. Uninstall it from **Settings › Apps › Installed apps**; the taskbar comes back straight away, and you're asked whether to keep your settings, pins, to-dos and notes.

To build the installer yourself you need the .NET 10 SDK and [Inno Setup 6](https://jrsoftware.org/isinfo.php):

```powershell
.\tools\build-installer.ps1
```

It writes the setup file to `publish\`.

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
- No code signing, so Windows SmartScreen may warn the first time the installer runs.
