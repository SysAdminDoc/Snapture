# Architecture

Snapture is a three-project .NET 10 WPF solution:

```
Snapture.sln
 ├── src/Snapture.Capture          Platform interop library (GDI/WinRT/D3D11/MF)
 ├── src/Snapture.App              Main WPF application
 └── src/Snapture.Plugin.Abstractions   Multi-target plugin SDK (netstandard2.0 + net10.0)
```

TFM: `net10.0-windows10.0.22621.0`, SupportedOSPlatform `10.0.17763.0`.

## Capture layer (`Snapture.Capture`)

The `ICaptureEngine` seam separates two engines:

- **WinRtCaptureEngine** — `Windows.Graphics.Capture` with `Direct3D11CaptureFramePool.CreateFreeThreaded`. Bridges D3D11 via 3 P/Invokes + a 6-method COM interop file (no Win2D, SharpDX, or Vortice). Captures via `IDirect3DDxgiInterfaceAccess`, stages to CPU-readable texture, checks for `WDA_EXCLUDEFROMCAPTURE` black surfaces.
- **GdiCaptureEngine** — `BitBlt`/`PrintWindow` fallback for pre-1809 or when WGC fails.

`CaptureEngineFactory` resolves the active engine from settings (`auto`/`winrt`/`gdi`). Hot-swap at runtime via `AppHost.SwitchEngine()`.

Supporting modules:
- `CaptureItemFactory` — picker-bypass `IGraphicsCaptureItemInterop` for window/monitor targets
- `MonitorEnumerator` / `WindowEnumerator` — system resource enumeration
- `ImageStitcher` — subsampled-SAD seam alignment for scrolling-capture stitching + sticky-strip detection
- `MFInterop` — Media Foundation COM declarations for video recording (SinkWriter, codec enumeration)
- `D3D11Interop` — D3D11 vtable slot calls (CreateTexture2D=5, CopyResource=47, Map=14, Unmap=15)

## Application layer (`Snapture.App`)

### Composition root

`AppHost` is the composition root. Wires hotkeys, capture engine, settings, history DB, orphan detector, LAN share, and plugin loader. `App.xaml.cs` creates the `AppHost` in `OnStartup`, sets AUMID, applies theme, requests borderless consent.

### Capture pipeline

`CaptureOrchestrator` routes tray/hotkey actions → engine capture → save → clipboard → editor. Modes: region, window, fullscreen, monitor, scrolling, smart-element, text-OCR, self-timer, quick-mode.

### Editor domain

- `AnnotationDocument` — background `SKBitmap` + ordered `ObservableCollection<Shape>`
- `Shapes.cs` — 12 polymorphic shape types (Rectangle, Ellipse, Line, Arrow, Freehand, Text, Highlight, Blur, Redact, Step, Spotlight, Ruler) with JSON discriminators for `.snapture` persistence
- `CommandStack` — Add/Remove undo/redo
- `SnapFileFormat` — `.snapture` = zip of `document.json` + `background.png` + `manifest.json`
- `AutoRedactor` + `SecretDetector` — Gitleaks-derived rule pack OCR-driven word-box redaction

The editor canvas (`EditorWindow.xaml.cs`) renders into `SkiaSharp.Views.WPF.SKElement`. Supports 15 tools, brightness/contrast/grayscale/invert adjustments, frame wrappers (drop shadow, rounded corners, gradient backdrop, Carbon code-chrome), transform handles for resize, and SVG export.

### Recording pipeline

`VideoRecorder` captures via WGC continuous frame pool → Media Foundation SinkWriter:
- Codec discovery chain: AV1 HW → HEVC HW/SW → H.264 HW/SW (software AV1 excluded)
- Fragmented MP4 (2-second `moof`/`mdat` fragments) for crash recovery
- Mixed AAC audio from WASAPI loopback + microphone via `RecordingAudioMixer`
- Process-tree loopback via `ProcessLoopbackCapture` (Win11+)
- Cursor highlight + click ring overlay (`CursorOverlayRenderer`)
- Keystroke overlay (`KeystrokeOverlayRenderer`, `RecordingKeyboardTracker`)
- Dirty-region frame skip (`DirtyRegionFrameFilter`, `DirtyRegionInterop`)
- Quality presets (Low/Medium/High/Ultra) and output resolution scaling (720p–4K)
- Environment resilience: source-closed, power suspend/resume, display-settings-changed

`GifRecorder` is simpler: in-memory `List<Bitmap>` → `AnimatedGif` library encode.

### History

`CaptureHistoryService` manages SQLite + FTS5 at `%LOCALAPPDATA%\Snapture\history\index.db`. Auto-tags by process name + window title. Schema-versioned via `PRAGMA user_version` + `SchemaVersionMigrator`.

### Services

| Service | Purpose |
|---------|---------|
| `SettingsService` | JSON at `%APPDATA%\Snapture\settings.json`, reload-on-startup, save-on-change |
| `ThemeManager` | Catppuccin Mocha (dark) / Latte (light) / System with semantic `App*` tokens |
| `HotkeyService` | `RegisterHotKey` on message-only `HwndSource` |
| `LanShareServer` | Kestrel minimal API, single-adapter, single-fetch tokens, TTL-bounded |
| `PluginLoader` | Collectible `AssemblyLoadContext`s, capability flags |
| `OcrService` | `Windows.Media.Ocr` wrapper |
| `DiagnosticDump` | PowerToys Bug Report Tool pattern support dump |
| `OrphanFileDetector` | Startup sweep for stale sessions/temp files |

### Plugin SDK (`Snapture.Plugin.Abstractions`)

Multi-target `netstandard2.0` + `net10.0`. Contracts:
- `IDestination` — upload/export targets
- `ICaptureProcessor` — pre-save processing (may resize)
- `IEditorEffect` — editor effects
- `IPluginHost` — host services surface
- `[SnapturePlugin]` attribute with capability flags

### Threading model

- WPF UI thread: all views, settings, tray
- WGC frame-arrived callback: free-threaded frame pool, writes samples under `lock`
- Audio mixer: dedicated async loop (20ms chunks) on `Task.Run`
- Pointer/keyboard tracking: dedicated background threads
- LAN share server: Kestrel on ASP.NET Core thread pool

### Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| SkiaSharp.Views.WPF | 3.119.2 | Editor canvas rendering |
| CommunityToolkit.Mvvm | 8.4.2 | MVVM support |
| Microsoft.Data.Sqlite | 10.0.9 | History database |
| NAudio | 2.3.0 | Audio capture (WASAPI) |
| Serilog | 4.3.1 | Structured logging |
| Hardcodet.NotifyIcon.Wpf | 2.0.1 | System tray |
| AnimatedGif | 1.0.5 | GIF encoding |
| Microsoft.AspNetCore.App | (framework) | LAN share Kestrel server |
