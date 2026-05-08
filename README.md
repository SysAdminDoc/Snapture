<!-- banner -->
<p align="center">
  <img src="branding/banner.png" alt="Snapture" width="100%" onerror="this.style.display='none'">
</p>

<h1 align="center">Snapture</h1>

<p align="center">
  <strong>The all-in-one screenshot utility for Windows.</strong><br>
  Region · Window · Fullscreen · Pinned overlays · Local-first · No telemetry · No cloud required.
</p>

<p align="center">
  <img alt="Version"  src="https://img.shields.io/badge/version-0.1.0-CBA6F7?style=for-the-badge">
  <img alt="License"  src="https://img.shields.io/badge/license-MIT-A6E3A1?style=for-the-badge">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2010%2F11-89B4FA?style=for-the-badge&logo=windows&logoColor=white">
  <img alt=".NET 10"  src="https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white">
</p>

---

## Why another screenshot tool?

The existing landscape on Windows in 2026:

| Tool | Strength | The catch |
|---|---|---|
| **Greenshot** | Mature plugin ecosystem | WinForms-era UI, GDI-only, weak on HDR/per-monitor DPI |
| **ShareX** | Maximum knobs | 1990s submenu maze, no theming, intimidating |
| **Flameshot** | Nice annotation | Windows port is second-class, DPI bugs |
| **Snipping Tool** | Built in | No scrolling, weak annotation, MS-opinionated UI |
| **Snagit** | Polished editor (paid) | $63/yr, pushes you toward TechSmith cloud |
| **CleanShot X** | Pinned overlay UX (paid) | Cloud-first, $29/yr |

**Snapture's pitch:** the polish of Snagit, the no-cloud philosophy of Greenshot, modern WinRT-class capture, and a Catppuccin Mocha editor that doesn't look like it was designed in 2008.

## What ships in v0.1.0

- **Region capture** — drag to select with live size readout, Catppuccin-themed overlay, ESC to cancel
- **Window capture** — captures the foreground window via `PrintWindow` with `PW_RENDERFULLCONTENT` (works through occlusion)
- **Fullscreen / Per-monitor capture** — multi-monitor enumeration with DPI-aware bounds
- **Global hotkeys** — `PrintScreen` (region), `Alt+PrintScreen` (window), `Ctrl+PrintScreen` (fullscreen)
- **Tray-resident** — left-click for region, right-click for full menu
- **Editor window** — view, save (PNG/JPG/BMP), copy, pin, show-in-folder
- **Pin to desktop** — borderless always-on-top window with scroll-to-zoom (Ctrl+scroll = opacity), drag to move, right-click to close
- **Settings persistence** — JSON at `%APPDATA%\Snapture\settings.json`
- **Crash log** — uncaught exceptions written to `%APPDATA%\Snapture\crashlog.txt`
- **Catppuccin Mocha** — across every window, dialog, and overlay

## What's coming (selected)

See [ROADMAP.md](ROADMAP.md) for the full picture.

- **v0.2** — WinRT capture engine (`Windows.Graphics.Capture`), full annotation editor (rect/arrow/text/blur/highlight/step-numbers), settings dialog
- **v0.3** — Scrolling capture (UIA + image-stitch), built-in OCR (Windows.Media.Ocr), GIF / MP4 record
- **v0.4** — UIA Smart Capture, auto-redact-secrets pass, LAN-only share server, plugin SDK

## Install

### From source

```bash
git clone https://github.com/SysAdminDoc/Snapture.git
cd Snapture
dotnet build -c Release Snapture.sln
dotnet run --project src/Snapture.App -c Release
```

Requirements: Windows 10 1903+ or Windows 11, .NET 10 SDK.

### Release builds

Download from [Releases](https://github.com/SysAdminDoc/Snapture/releases) once the first tag is cut.

## Usage

After launching, Snapture lives in the system tray.

| Hotkey | Action |
|---|---|
| `PrintScreen` | Capture region |
| `Alt+PrintScreen` | Capture foreground window |
| `Ctrl+PrintScreen` | Capture fullscreen (all monitors) |
| Tray left-click | Capture region |
| Tray right-click | Full menu (per-monitor capture, output folder, about, quit) |

Captures are saved to `%USERPROFILE%\Pictures\Snapture\` by default and copied to the clipboard. The editor window opens after every capture (configurable in `settings.json`).

## Configuration

`%APPDATA%\Snapture\settings.json`:

```json
{
  "outputFolder": "C:\\Users\\you\\Pictures\\Snapture",
  "filenamePattern": "Snapture_{yyyy-MM-dd}_{HH-mm-ss}",
  "outputFormat": "PNG",
  "copyToClipboard": true,
  "openEditorAfterCapture": true,
  "showToastOnSave": true,
  "launchAtStartup": false
}
```

`filenamePattern` accepts any .NET `DateTime` format token inside `{...}`.

## Architecture

```
Snapture.sln
├─ src/
│  ├─ Snapture.Capture/          ← Capture engine library
│  │  ├─ ICaptureEngine          ← Async capture contract
│  │  ├─ GdiCaptureEngine        ← v0.1: GDI / PrintWindow
│  │  ├─ MonitorEnumerator       ← Per-monitor DPI awareness
│  │  └─ WindowEnumerator        ← Top-level window listing + hit-test
│  └─ Snapture.App/              ← WPF shell
│     ├─ App.xaml(.cs)           ← Entry, crash logging
│     ├─ Services/
│     │  ├─ AppHost              ← Lifetime, DI-lite, hotkey wiring
│     │  ├─ CaptureOrchestrator  ← Capture → save → clipboard → editor
│     │  ├─ HotkeyService        ← RegisterHotKey on a message-only window
│     │  ├─ SettingsService      ← JSON load/save with safe defaults
│     │  └─ TrayIconHost         ← NotifyIcon + context menu
│     └─ Views/
│        ├─ RegionOverlayWindow  ← Frozen-screen drag-to-select
│        ├─ EditorWindow         ← View / save / copy / pin
│        └─ PinWindow            ← Always-on-top borderless overlay
└─ Resources/Themes/CatppuccinMocha.xaml
```

## License

[MIT](LICENSE)
