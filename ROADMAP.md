# Snapture Roadmap

The build philosophy: **WinRT-first, no cloud, no telemetry, polish that beats Snagit, knobs that beat ShareX.**

Items use `[ ]` (open) / `[x]` (shipped). Progress lands in CHANGELOG with version tags.

## v0.2 — WinRT engine + annotation editor

### Capture engine
- [ ] `WinRtCaptureEngine` using `Windows.Graphics.Capture` (per-monitor DPI, HDR, no flicker)
  - [ ] D3D11 device + IDirect3DDevice interop
  - [ ] `Direct3D11CaptureFramePool` with single-frame mode
  - [ ] BGRA8 → managed bitmap converter
  - [ ] Engine selection in settings (`winrt` / `gdi`)
  - [ ] Auto-fallback to GDI on Windows < 10 1903 or capture failure
- [ ] HDR-aware tone mapping (DXGI swap-chain colorspace metadata)
- [ ] Window selection mode with hover highlight (overlay shows bounds of window under cursor; PgUp/PgDn walks parent/child chain)

### Annotation editor (full)
- [ ] Vector annotation layer (every shape stays editable forever; flatten only on export)
- [ ] Tools: select, rect (filled/outlined), ellipse, line, arrow, freehand, text, highlight, blur/pixelate, step-number, ruler, eyedropper, crop
- [ ] Hotkeys: V/R/E/L/A/F/T/H/O/N/M/I/C
- [ ] Undo/redo (unlimited, per-document)
- [ ] Recent colors bar
- [ ] Border / shadow / rounded-corner export wrappers
- [ ] Brightness / contrast / grayscale / invert adjustments
- [ ] Export: PNG, JPG, BMP, SVG (vector when no rasterized adjustments), `.snapture` project file

### Settings dialog
- [ ] Tabs: General · Capture · Hotkeys · Output · Editor · Frame · Advanced
- [ ] Live hotkey recorder (click field → press combo → bound)
- [ ] Per-action hotkey customization
- [ ] Output filename template variable browser
- [ ] Engine selector with capability detection
- [ ] Import / export settings JSON

## v0.3 — Capture parity with the leaders

- [ ] Scrolling capture
  - [ ] UIA `IScrollProvider` driver path (browsers, Office, native lists)
  - [ ] Image-stitch fallback with phase-correlation alignment (works on parallax)
  - [ ] Live preview during scroll
- [ ] Built-in OCR
  - [ ] `Windows.Media.Ocr` for in-box recognition
  - [ ] Optional Tesseract bundle for languages not installed
  - [ ] OCR-region capture returns image AND recognized text in clipboard
  - [ ] Text overlay anchored to image regions (Shottr-style UX)
- [ ] GIF recording
  - [ ] `Windows.Graphics.Capture` continuous frames
  - [ ] Frame timeline picker, trim, optimize palette
- [ ] MP4 recording
  - [ ] Media Foundation `SinkWriter` with H264 encoder
  - [ ] Cursor highlight overlay
  - [ ] System audio + mic capture (WASAPI loopback)
- [ ] Last-region recapture (`Shift+PrintScreen`)
- [ ] Timed capture: select region first, countdown, then refresh-capture (for menus/tooltips)
- [ ] Color-picker global hotkey (captures pixel under cursor as hex to clipboard)
- [ ] Capture history thumbnail panel (`%APPDATA%\Snapture\history\`)

## v0.4 — Differentiators no incumbent ships

- [ ] **UIA Smart Capture** — hover any window, see live highlights of every UIA element, click-pick exact button/panel for pixel-perfect crop (Snagit Smart Move-class, free)
- [ ] **Auto-redact secrets pass** — local regex + ONNX text-detection model scans every capture for API keys / JWTs / AWS keys / emails / IPs / credit cards / MAC addresses; shows "N secrets detected — review or auto-blur" toast; per-rule toggles; zero cloud
- [ ] **LAN-only share server** — built-in Kestrel HTTP server (opt-in, bound to selected adapter), serves `http://your-pc:port/<token>/<id>.png`, mDNS announce optional. Copy a LAN link to Slack/Teams without uploading anywhere
- [ ] **Code-aware capture** — detect code blocks in capture region, OCR + syntax highlight as bonus output
- [ ] **Pinned overlay multi-canvas** — multiple pins snap-arrange into a comparison board with saved layouts
- [ ] **Plugin SDK** — drop-in `%APPDATA%\Snapture\Plugins\*.dll` with `IDestination` / `IEditorTool` contracts; backwards-compatible with Greenshot destination plugin shape

## v0.5 — Distribution polish

- [ ] MSIX package + winget manifest
- [ ] Portable-mode flag (settings next to exe for USB stick use)
- [ ] Context-menu shell integration ("Open in Snapture editor" on right-click)
- [ ] Auto-update via signed GitHub Releases
- [ ] Per-app capture profiles (preset per foreground app)
- [ ] CLI mode (`snapture --region 0,0,800,600 --out file.png`)

## Stretch / nice-to-haves

- [ ] Watch folder — auto-ingest images dropped into a configured folder
- [ ] Magic wand / heal tool (LaMa ONNX) for object removal
- [ ] Camera overlay + green-screen keying for "me + screen" tutorial captures
- [ ] Markdown clipboard-copy ("paste as `![alt](path)`")
- [ ] Linux port (X11 + Wayland) — far horizon

## Competitive watch list

- **Greenshot** — baseline reference; do not re-add the plugin bloat (Imgur/Dropbox/Box/Flickr/Jira/Confluence)
- **ShareX** — destination concept is fine *if* user-configured per endpoint; never bundle 80+
- **Flameshot** — selection-anchored toolbar UX (rotates with selection) is worth borrowing for the annotation editor
- **CleanShot X** — pinned-overlay polish; watch for new differentiators
- **Snagit** — Smart Move (UI-element-aware capture) is the feature to match in v0.4
- **Shottr** — OCR-overlay anchored to image regions; mirror in v0.3
