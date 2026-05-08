# Changelog

All notable changes to Snapture will be documented in this file.

## [v0.2.0] — 2026-05-08

The "real screenshot tool" pass: WinRT capture engine, full annotation editor, settings dialog, and the polish features that competing OSS tools either paywall or skip entirely.

### Added — Capture (v0.2.1)

- `WinRtCaptureEngine` using `Windows.Graphics.Capture` with free-threaded frame pool and BGRA8 staging-texture readback. No Win2D / Vortice / SharpDX dependency — D3D11 → `IDirect3DDevice` bridge is 3 P/Invokes plus one COM interface (`IDirect3DDxgiInterfaceAccess`).
- `CaptureEngineFactory` resolves `auto` / `winrt` / `gdi` from settings; auto-falls-back to GDI on Win10 < 1809 or WGC failure. Engine is hot-swappable from the tray menu and the settings dialog.
- `CaptureItemFactory` picker bypass via `IGraphicsCaptureItemInterop.CreateForWindow` / `CreateForMonitor`.
- `AppIdentity.SetAumid()` runs first thing in `App.OnStartup` so the borderless-capture consent persists across reinstalls.
- `BorderlessConsent.RequestAsync()` first-run prompt on Win11 22H2+. Result is persisted in `settings.json`.
- `WDA_EXCLUDEFROMCAPTURE` detection: 16-pixel sample raises `CaptureExcludedException` instead of saving a black PNG when the OS marks a window excluded.
- `WindowPickerWindow` — hover-highlight overlay, click to pick, PgUp/PgDn to walk the ancestor chain, non-activating so the target window keeps focus.
- `PrintScreenHijackDetector` — registry probe for Win11 24H2's `PrintScreenKeyForSnippingEnabled`. Tray surfaces a one-click "Reclaim PrintScreen" entry when the value is set.
- `Shift+PrintScreen` recaptures the last region (persisted across restart).
- Tray "Capture with Delay" submenu — 1 / 3 / 5 / 10s self-timer for region capture.

### Added — Annotation editor (v0.2.2)

- `AnnotationDocument` + polymorphic `Shape` model — every shape stays editable forever, flattened only on raster export. JSON discriminator `kind` for round-trip.
- SkiaSharp canvas substrate (`SkiaSharp.Views.WPF`).
- Tools: Rectangle (filled / outlined / rounded), Ellipse, Line (straight / dashed), Arrow (straight / bidirectional / dashed), Freehand pen with mouse-wheel thickness, Text, Highlight, Blur, Pixelate, solid-fill Redact, auto-incrementing Step counter.
- Hotkeys: `V/R/E/L/A/F/T/H/B/X/N/C` for tool selection; `Ctrl+Z`/`Y` undo/redo; `Ctrl+S` save .snapture; `Ctrl+E` export PNG; `Ctrl+O` open.
- 12-swatch color palette + recent-colors bar (last 6).
- Brightness / contrast / grayscale / invert raster adjustments.
- Drop shadow / rounded corners / gradient backdrop frame wrappers — preview and export render identically.
- Export to PNG / JPG / BMP / WebP via `SKImage.Encode`.
- `.snapture` project file format (zip = `document.json` + `background.png` + `manifest.json`) — round-trips losslessly.
- "Open existing image" via File → Open (PNG / JPG / BMP / `.snapture`).

### Added — Settings dialog (v0.2.3)

- Tabbed `SettingsWindow` (General / Capture / Hotkeys / Output / Advanced).
- Live hotkey recorder per action — region / window / fullscreen / last-region all rebindable.
- `AppHost.RewireHotkeys()` re-applies bindings without restart.
- Engine selector with capability detection (greys out WinRT on Win10 < 1809).
- Output filename template input with DateTime placeholder reference.
- Borderless-consent retry button + Reclaim-PrintScreen button on the Capture tab.
- Settings JSON import/export, reveal-in-Explorer, runtime diagnostics (OS / .NET / engine / monitor count / AUMID).

### Added — Capture polish (v0.2.4)

- `ColorPickerWindow` — HEX / RGB / HSL / APCA-Lc readout (vs white + vs black). Live cursor sample via `Graphics.CopyFromScreen`. Low-level mouse hook captures clicks anywhere on screen to lock the colour and copy HEX.
- `PixelRulerWindow` — drag to measure Δx / Δy / pixel length / angle across the entire virtual screen.
- Magnifier loupe in `RegionOverlayWindow` — 6× zoom of a 20×20 source patch, crosshair, pixel coordinate + HEX readout, auto-flips to opposite quadrant near screen edges.
- Pin window polish — opacity submenu (25/50/75/100%) plus Ctrl-scroll, border / shadow toggles (`B` / `S`), `Alt+click` toggles `WS_EX_TRANSPARENT` click-through, `O` solo-mode, `H` hide / show all pins.

### Changed

- TFM bumped to `net10.0-windows10.0.22621.0` with `SupportedOSPlatformVersion=10.0.17763.0` so 22H2 toggles (`IsBorderRequired`, `IncludeSecondaryWindows`) compile cleanly.
- `AllowUnsafeBlocks=true` on both projects (D3D11 vtable invocation + raster pixel passes in adjustments).
- Tray menu reorganised: capture actions, monitor list, self-timer, tools, settings, engine selector, about, quit.

### Security & privacy

- No telemetry added. The only network calls available are still the user-initiated GitHub release check (off by default) and the `BorderlessConsent.RequestAsync` system call which is local. Codepath audit: zero `HttpClient` / `WebRequest` use in `Snapture.App` or `Snapture.Capture`.

### Architecture notes

- Capture seam (`ICaptureEngine`) unchanged — the WinRT engine drops in alongside GDI without touching the orchestrator.
- D3D11 vtable slots in use: `CreateTexture2D` (5), `CopyResource` (47), `Map` (14), `Unmap` (15). These come from `D3d11.h` and won't move — D3D11 is frozen.
- The `GraphicsCaptureItem` activation factory is reachable via `Marshal.GetObjectForIUnknown` because `IGraphicsCaptureItemInterop` is `IUnknown`-derived. If a future .NET upgrade breaks this, switch to `WinRT.MarshalInspectable<T>.FromAbi(ptr)`.

## [v0.1.0] — 2026-05-08

Initial release. Vertical slice of the all-in-one screenshot utility.

### Added
- Region capture with frozen-screen overlay (drag to select, live size readout, ESC to cancel, Enter to confirm).
- Window capture via `PrintWindow(PW_RENDERFULLCONTENT)` — captures occluded windows.
- Fullscreen / per-monitor capture with DPI-aware bounds.
- Global hotkeys: `PrintScreen` (region), `Alt+PrintScreen` (window), `Ctrl+PrintScreen` (fullscreen).
- System tray icon with full context menu (per-monitor capture, output folder, quit).
- Editor window: view, save (PNG/JPG/BMP), copy, pin, show-in-folder.
- Pin window: borderless always-on-top, drag to move, scroll to zoom, Ctrl+scroll for opacity.
- Settings persistence at `%APPDATA%\Snapture\settings.json`.
- Crash log at `%APPDATA%\Snapture\crashlog.txt`.
- Catppuccin Mocha theme across every window.
- `Snapture.Capture` library with `ICaptureEngine` abstraction (GDI implementation).
- `Snapture.App` WPF shell on .NET 10.

### Architecture
- `ICaptureEngine` is the seam for v0.2's WinRT engine — implementations swap without touching the orchestrator.
- WPF `HwndSource` message-only window hosts `RegisterHotKey` callbacks.
- `Hardcodet.NotifyIcon.Wpf` for the tray; `CommunityToolkit.Mvvm` queued for v0.2 editor MVVM.
