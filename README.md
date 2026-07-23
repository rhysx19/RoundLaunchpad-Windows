# RoundLaunchpad (Windows)

Windows rewrite of [RoundLaunchpad](https://github.com/rhysx19/RoundLaunchpad) — a system-tray app that opens a full-screen cosmos with your favorite apps in a glowing ring.

Built with **.NET 8 + WPF**. Designed to match the Mac app’s behavior and visuals as closely as practical.

## Download

Every push to `main` builds on GitHub Actions. Grab the zip from the latest successful run:

**Actions → Build → latest green run → Artifacts → `RoundLaunchpad-win-x64`**

Or from a Release if one has been published.

Unzip and run `RoundLaunchpad.exe`. No installer required (self-contained build).

## Usage

| Input | Action |
|--------|--------|
| **Alt+Space** | Open / toggle the ring |
| **Hold Alt+Space, hover, release** | Launch without clicking |
| **Click** an icon | Warp-launch that app |
| **Arrow keys** | Move selection |
| **Enter** | Launch selection |
| **1–9** | Launch by position |
| **Esc** / click background | Dismiss |
| Tray icon → **Settings…** | Edit apps & options |

If Alt+Space is already taken by another app, the hotkey falls back to **Ctrl+Alt+Space**.

> Note: Classic Windows uses Alt+Space for the window system menu. While RoundLaunchpad is running it claims Alt+Space globally (same idea as the Mac app claiming ⌥Space).

### Settings

- **Double-tap Alt opens the ring** — optional; uses a low-level keyboard hook (AV may prompt once).
- **Open the ring at the mouse pointer**
- **Launch at login** — HKCU Run key
- Add `.exe` or Start Menu `.lnk` shortcuts; reorder with ↑/↓

### Config

```
%APPDATA%\RoundLaunchpad\config.json
```

## Build locally (Windows)

```powershell
dotnet publish src\RoundLaunchpad\RoundLaunchpad.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
.\publish\RoundLaunchpad.exe
```

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

## Project layout

```
src/RoundLaunchpad/
  App.xaml(.cs)          Startup, single-instance
  AppController.cs       Tray, hotkeys, window lifecycle
  Services/              Settings, icons, launch, hotkeys, shortcuts
  Views/                 Launcher overlay, cosmos, settings
  Models/                Config + session state
```

## Mac parity

| Feature | Status |
|---------|--------|
| Tray / menu-bar presence | ✅ System tray |
| Alt+Space open + hold-to-launch | ✅ |
| Double-tap Alt | ✅ Optional |
| Cosmos + planets + meteors + warp streak | ✅ |
| Radial ring, beam, glow-from-icon | ✅ |
| Running-app dot | ✅ Best-effort |
| Activate running app | ✅ Best-effort |
| Settings + config JSON | ✅ |
| Launch at login | ✅ |
| Open at mouse / multi-monitor | ✅ |

## License

Personal project — same ownership as the Mac RoundLaunchpad repo.
