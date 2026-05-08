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
  <img alt="Version"  src="https://img.shields.io/badge/version-0.2.0-CBA6F7?style=for-the-badge">
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

## What ships in v0.2.0

**Capture (v0.2.1)**

- **WinRT capture engine** — `Windows.Graphics.Capture` with free-threaded frame pool, BGRA8 staging-texture readback, no flicker, no Win2D / Vortice / SharpDX dependency. Auto-falls-back to GDI on Win10 < 1809 or capture failure.
- **Engine selector** — `auto` / `winrt` / `gdi` in settings + the tray menu, hot-swappable at runtime.
- **AppUserModelID** set on app start so the borderless-capture consent persists across reinstalls.
- **First-run consent** for `GraphicsCaptureAccess.RequestAccessAsync(Borderless)` on Win11 22H2+.
- **WDA_EXCLUDEFROMCAPTURE-aware** — flags windows the OS marks excluded (1Password / Bitwarden / banking) instead of saving black PNGs.
- **Window-pick overlay** with hover highlight + PgUp/PgDn ancestor walk.
- **PrintScreen-on-Win11-24H2** detection — tray surfaces a one-click "Reclaim PrintScreen" entry when the OS hijacks the key.
- **Last-region recapture** (`Shift+PrintScreen`) and **self-timer** (1/3/5/10s region capture) in the tray.

**Annotation editor (v0.2.2)**

- **SkiaSharp-backed canvas** with a vector annotation document model — every shape stays editable forever, flattened only on raster export.
- **Tools**: Rectangle (filled / outlined / rounded), Ellipse, Line (straight / dashed), Arrow (straight / bidirectional / dashed), Freehand pen with mouse-wheel thickness, Text, Highlight, Blur, Pixelate, solid-fill Redact, Step counter (auto-increment).
- **Hotkeys**: `V/R/E/L/A/F/T/H/B/X/N/C` plus `Ctrl+Z`/`Y` undo/redo, `Ctrl+S` save .snapture, `Ctrl+E` export PNG, `Ctrl+O` open.
- **Beautify wrappers**: drop shadow, rounded corners, gradient backdrop — preview and export render identically.
- **Adjustments**: brightness, contrast, grayscale, invert.
- **Export**: PNG, JPG, BMP, WebP.
- **`.snapture` project file** — zip = `document.json` + `background.png` + `manifest.json`. Round-trips losslessly.
- **Open existing image** into the editor (PNG / JPG / BMP / `.snapture`).

**Settings dialog (v0.2.3)**

- Tabbed dialog (General / Capture / Hotkeys / Output / Advanced) reachable from the tray.
- **Live hotkey recorder** — click a field, press combo, bound. Region / window / fullscreen / last-region all rebindable, applied without restart.
- **Engine selector** with capability detection.
- **Output filename template** with placeholder reference.
- **Borderless-consent retry** + **Reclaim-PrintScreen** buttons on the Capture tab.
- **Settings JSON import/export** + reveal in Explorer + runtime diagnostics.

**Capture polish (v0.2.4)**

- **Color picker tool** — HEX / RGB / HSL / APCA-Lc readout (vs white + vs black). Live cursor sample, click anywhere to lock + copy HEX.
- **Pixel ruler tool** — drag to measure Δx / Δy / pixel length / angle across the entire virtual screen.
- **Magnifier loupe** in the region overlay — 6× zoom, crosshair, pixel coordinate + HEX readout, auto-flips at screen edges.
- **Pin window polish** — opacity submenu (25/50/75/100% plus Ctrl-scroll), border / shadow toggles, `Alt+click` click-through, `O` solo mode, `H` hide/show all pins.

## What's coming (selected)

See [ROADMAP.md](ROADMAP.md) for the full picture.

- **v0.3** — Scrolling capture (UIA + image-stitch), built-in OCR (Windows.Media.Ocr), GIF / MP4 record, HDR tonemap (ACES) + AVIF / JPEG XR, capture history with FTS5
- **v0.4** — UIA Smart Capture, auto-redact-secrets pass, LAN-only share server, plugin SDK
- **v0.5** — MSIX + winget + Chocolatey + portable ZIP, code-signing via SignPath OSS, auto-update via Velopack

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
| `Shift+PrintScreen` | Recapture last region |
| Tray left-click | Capture region |
| Tray right-click | Full menu (per-monitor, settings, tools, engine, output folder) |

All four hotkeys are rebindable from **Settings → Hotkeys**.

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
│  ├─ Snapture.Capture/                ← Capture engine library
│  │  ├─ ICaptureEngine                ← Async capture contract
│  │  ├─ GdiCaptureEngine              ← GDI / PrintWindow fallback
│  │  ├─ WinRtCaptureEngine            ← Windows.Graphics.Capture (v0.2)
│  │  ├─ CaptureItemFactory            ← IGraphicsCaptureItemInterop picker bypass
│  │  ├─ D3D11Interop                  ← D3D11 + IDirect3DDevice bridge (3 P/Invokes)
│  │  ├─ MonitorEnumerator             ← Per-monitor DPI awareness
│  │  └─ WindowEnumerator              ← Top-level window listing + hit-test
│  └─ Snapture.App/                    ← WPF shell
│     ├─ App.xaml(.cs)                 ← Entry, AUMID, crash logging
│     ├─ Services/
│     │  ├─ AppHost                    ← Lifetime, hotkey wiring, engine swap
│     │  ├─ CaptureOrchestrator        ← Capture → save → clipboard → editor
│     │  ├─ CaptureEngineFactory       ← auto/winrt/gdi resolver
│     │  ├─ HotkeyService              ← RegisterHotKey on a message-only window
│     │  ├─ SettingsService            ← JSON load/save
│     │  ├─ TrayIconHost               ← NotifyIcon + context menu
│     │  ├─ AppIdentity                ← Sets AUMID for borderless-consent persistence
│     │  ├─ BorderlessConsent          ← Win11 22H2+ first-run prompt
│     │  └─ PrintScreenHijackDetector  ← 24H2 registry probe + reclaim
│     ├─ Editor/
│     │  ├─ AnnotationDocument         ← Background bitmap + ordered shape list
│     │  ├─ Shapes                     ← Rect / Ellipse / Line / Arrow / Pen / Text / Highlight / Blur / Redact / Step (polymorphic JSON)
│     │  ├─ CommandStack               ← Add / Remove undo/redo
│     │  └─ SnapFileFormat             ← .snapture zip = document.json + background.png + manifest.json
│     └─ Views/
│        ├─ RegionOverlayWindow        ← Frozen-screen drag-to-select with magnifier loupe
│        ├─ WindowPickerWindow         ← Hover-highlight + PgUp/PgDn ancestor walk
│        ├─ EditorWindow               ← Skia canvas + tool palette + adjustments + frame wrappers
│        ├─ PinWindow                  ← Always-on-top + opacity / shadow / click-through / solo / hide-all
│        ├─ SettingsWindow             ← Tabbed dialog (General/Capture/Hotkeys/Output/Advanced)
│        ├─ ColorPickerWindow          ← HEX / RGB / HSL / APCA-Lc + global click capture
│        └─ PixelRulerWindow           ← Δx / Δy / length / angle across the virtual screen
└─ Resources/Themes/CatppuccinMocha.xaml
```

## License

[MIT](LICENSE)
