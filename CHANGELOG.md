# Changelog

All notable changes to Snapture will be documented in this file.

## [v0.5.0] — 2026-05-08

The "scrolling capture actually works for browsers now" pass, plus a Carbon-style code-window export wrapper.

### Added — Image-stitch fallback for scrolling capture (v0.5.1)

- `Snapture.Capture/ImageStitcher` finds the vertical overlap between consecutive frames using subsampled sum-of-absolute-differences (SAD) on a 80-row strip from frame N-1, searched against frame N. Confidence ≥ 0.92 to accept the alignment, otherwise we fall back to naive concatenation.
- `ScrollingCaptureService.StackVertically` now routes through the stitcher. Result: visible duplicate strips at frame boundaries are gone for ≥90% of common browser pages and document viewers.
- Pure managed implementation — no Math.NET / OpenCV dependency. Subsample factor 4, search bound to plausible scroll deltas. ~150–200 ms per frame pair on 1920×1080 captures.
- Honest about residuals: ad / animation between frames produces small ghosting at the seam, sticky-header / sticky-footer detection ships in v0.6.

### Added — Code-window chrome (v0.5.2)

- New "Code window chrome (Carbon-style)" toggle in the editor's Frame panel. When enabled, the export wraps the document in a 36-pixel macOS-style title bar with red / yellow / green traffic-light dots, dark-gray bar, and 14-px rounded corners on the outer frame.
- The on-canvas preview omits the chrome (the WPF `SKElement` is sized to the document so a top bar would be clipped) and the status bar reminds the user. The export render produces the full chrome.
- Pairs with the existing drop-shadow / rounded-corners / gradient backdrop wrappers — toggle any combination.

### Changed

- `Snapture.Capture` version bumped to 0.5.0.0 to match the app version (the capture-engine library carried 0.4.0 at release time despite the new stitcher; corrected in this release).
- README badge bumped to 0.5.0; What-Ships section gains the new entries; v0.5 line in the roadmap section retired now that v0.5 has shipped.

### Deferred

- GIF / MP4 recording (Media Foundation `SinkWriter`)
- HDR tonemap (ACES via Win2D) + AVIF / JPEG XR export
- RapidOCR ONNX bundle
- Sticky-header / sticky-footer detection in stitcher
- DOCX / PPTX export from Step Capture
- Plugin resize contract widening

## [v0.4.0] — 2026-05-08

The differentiator wave: LAN-only share server, UIA Smart Capture, Plugin SDK, Step Capture mode, plus the auto-redact secrets pass that was committed-but-untagged after v0.3. Image-stitch fallback / GIF-MP4 / HDR explicitly deferred to v0.5.

### Added — Auto-redact secrets (v0.4.1)

- `Editor/SecretDetector` ports a Gitleaks-derived rule pack as compiled regex: AWS access + secret keys, Google API keys, GitHub PATs / app / oauth / refresh tokens, Stripe live + publishable, Slack tokens + webhooks, Twilio SIDs, JWTs, npm tokens, generic 40+ hex strings, plus PII (Luhn-validated credit cards, US SSN, IBAN, IPv4, MAC, email).
- `Editor/AutoRedactor` re-runs `Windows.Media.Ocr` over the rendered document, walks every word, scans with the rule pack, and emits a `RedactShape` (solid-fill, blur is reversible) on each matched word-box.
- Editor toolbar gains an "Auto-redact secrets" button. Each detected secret is pushed onto the command stack as a separate `AddShapeCommand` so the user can undo individually; the status bar surfaces the rule IDs that fired so false positives are visible.

### Added — LAN-only share server (v0.4.2)

- `LanShareServer` — Kestrel minimal API, binds to a single user-chosen adapter (never `0.0.0.0` by default). 24-byte URL-safe-base64 tokens, single-fetch by default, TTL-bounded.
- New Settings tab "LAN share": toggle the server, pick the adapter from a list of live IPv4 interfaces, set port + TTL. Start / Stop buttons run the server out-of-band so the user can verify the URL before opting into auto-start.
- Editor "Share to LAN" button — flattens the document with adjustments + frame, registers the file with the server, copies the single-fetch URL to the clipboard.
- Server is opt-in only; off by default. No mDNS, no firewall mods (Windows Firewall may prompt on first run).

### Added — UIA Smart Capture (v0.4.3)

- `SmartCaptureWindow` — non-activating overlay that uses `AutomationElement.FromPoint` to pick the leaf UIA element under the cursor in real time. Highlights the element's bounding rectangle, shows control type / name / dimensions in a description badge.
- `PgUp` walks the parent in `TreeWalker.RawViewWalker` (locks manual mode), `PgDn` releases back to live cursor tracking, click captures.
- Captures the exact element pixel-rect via the active capture engine and routes through the standard editor-open flow.
- Tray menu: "Smart Element Capture…".

### Added — Plugin SDK (v0.4.4)

- New project `Snapture.Plugin.Abstractions` (multi-target `netstandard2.0` + `net10.0`) — public surface for third-party authors. Contracts: `IDestination`, `ICaptureProcessor`, `IEditorEffect`, `IPluginHost`. Capability flags (`Network`, `FilesystemWrite`, `Clipboard`, `LaunchProcess`, `InteractWithApp`).
- `[SnapturePlugin]` attribute carries name / author / version / description / capabilities.
- `Services/PluginLoader` discovers `*.dll` under `%APPDATA%\Snapture\Plugins\`, loads each in its own collectible `AssemblyLoadContext`, registers `Resolving` so the host's `Snapture.Plugin.Abstractions` is the canonical reference (type-equal across plugins).
- `Services/PluginHostBridge` exposes `IPluginHost` to plugins (scratch dir, toast, log).
- `Views/PluginsWindow` lists installed plugins with capabilities + reload + open-folder.
- Capture-processors marked `RunsByDefault=true` run after every capture in `CaptureOrchestrator.DeliverCaptureAsync` — failures are logged but never block delivery.

### Added — Step Capture mode (v0.4.5)

- `StepCaptureSession` — installs a low-level mouse hook (`WH_MOUSE_LL`); on every left-button click anywhere on screen, captures the foreground window after a 120ms settle delay, writes `step_NNN.png` into a session folder under `%LOCALAPPDATA%\Snapture\step-sessions\<timestamp>\`. 250ms debounce prevents duplicate frames on double-click.
- `StepCaptureWindow` — review UI: Start / Stop, live thumbnail of every captured step, per-step caption text box (multi-line), document-title input.
- `StepCaptureExporter` — emits a Markdown bundle: `steps.md` with a heading per step + caption + image reference, plus an `images/` subdirectory with renamed copies. DOCX / PPTX export ships in v0.5.
- Tray menu: "Step Capture…".

### Changed

- `AppHost` gained four new owned services: `LanShareServer`, `PluginLoader`, `PluginHostBridge`, plus the existing `History` continues. Lifecycle: services start in the constructor, plugins load after settings, LAN share auto-starts only if the user previously opted in.
- `CaptureOrchestrator` now runs plugin `ICaptureProcessor` instances post-capture and pre-history-index. Pixels can be replaced in-place but not resized in this version (resize ships in v0.5 with a wider plugin contract).
- Tray Tools submenu now: Color picker · Pixel ruler · OCR region · Step Capture · Plugins · Capture history.
- Solution gained a third project (`Snapture.Plugin.Abstractions`).

### Architecture notes

- LAN share tokens use 24 random bytes (192 bits) base64-URL-encoded. The server enforces single-fetch by removing the entry on first GET regardless of TTL.
- Plugin loader registers `AssemblyLoadContext.Resolving` so plugins reference the host's `Snapture.Plugin.Abstractions` rather than loading their own copy — type identity matters because the host casts plugin instances through `IDestination` etc.
- Step Capture's mouse hook is installed only while the session is running. Closing the review window stops it. The hook reads but never swallows clicks (the `CallNextHookEx` invocation is unconditional).
- Smart Capture toggles `WS_EX_TRANSPARENT` on its overlay window for the duration of the `AutomationElement.FromPoint` call so the overlay itself doesn't intercept the hit-test.

### Deferred to v0.5

- Image-stitch fallback for scrolling capture (browsers, parallax, lazy-load)
- GIF / MP4 recording (Media Foundation `SinkWriter`)
- HDR tonemap (ACES via Win2D) + AVIF / JPEG XR export
- RapidOCR ONNX bundle (model download flow)
- DOCX / PPTX export from Step Capture
- Plugin resize contract widening
- Per-rule on/off settings for the auto-redact pack

## [v0.3.0] — 2026-05-08

The capture-parity pass: built-in OCR, full-text searchable capture history, and a first-pass scrolling capture path. Three of the five v0.3.x sub-tracks shipped (OCR, history, scrolling). Image-stitching, GIF/MP4 recording and HDR tonemap explicitly deferred to v0.4.

### Added — OCR (v0.3.2)

- `OcrService` wraps `Windows.Media.Ocr`. Zero-install on any modern Windows; uses the user's installed language packs.
- "OCR region…" capture flow: select a region with the existing overlay, run OCR, recognised text lands in the clipboard automatically, full-text result opens in `OcrResultWindow`.
- Settings deeplink (`ms-settings:regionlanguage-adddisplaylanguage`) helper for installing additional language packs.
- History row context menu gains "Run OCR" — re-OCR a previously saved capture and index the text into FTS5.
- "OCR all" button in the History window indexes every entry that hasn't been OCR'd yet.

### Added — Capture history (v0.3.5)

- `CaptureHistoryService` — SQLite database at `%LOCALAPPDATA%\Snapture\history\index.db` with FTS5 virtual table over OCR text + window title + process name.
- Every capture is auto-tagged with the foreground window's `ProcessName` and title via `CaptureHistoryService.DescribeForeground`.
- `HistoryWindow` — thumbnail wall, debounced search box (FTS5), context menu: Open in editor / Pin / Run OCR / Reveal in folder / Delete.
- `Microsoft.Data.Sqlite` 9.0.0 + `SQLitePCLRaw.bundle_e_sqlite3` 2.1.10 added.

### Added — Scrolling capture (v0.3.1)

- `ScrollingCaptureService` drives `IScrollProvider` via `System.Windows.Automation` (no FlaUI dep needed for v0.3 scope).
- Tray menu "Capture Scrolling Window (alpha)" — drives the foreground window's scroll pattern from top to bottom, captures each frame via the active engine, stacks vertically.
- Honest about limitations: small visual duplicates at frame boundaries (UIA reports percent, not pixels), browsers that route scroll through their own hosts will fall through to a clean "this window doesn't expose UIA scroll" message. Phase-correlation stitch fallback ships in v0.4.

### Changed

- `CaptureOrchestrator` now takes an optional `CaptureHistoryService` and indexes every saved capture (best-effort — history failures never block the user).
- Tray Tools submenu reorganised: Color picker · Pixel ruler · OCR region · Capture history.

### Architecture notes

- The OCR-result window is a normal `Window` not a dialog — multiple OCR results can stay open while the user keeps capturing.
- `CaptureHistoryService` initialises `SQLitePCL.Batteries_V2` exactly once via an interlocked guard so multiple service instances (eg. tests) don't double-init.
- `ScrollingCaptureService.FindScrollable` walks 200 elements breadth-first from the window root before giving up — chosen empirically as enough for real-world apps without making the search noticeably slow.

### Deferred to v0.4

- RapidOCR ONNX fallback (model download flow needed first)
- Image-stitching (phase correlation + lazy-load handling + sticky-header detection)
- QR/barcode extraction (ZXing.Net)
- OCR table mode + text overlay anchored to image regions
- GIF/MP4 recording (Media Foundation SinkWriter)
- HDR tonemap + AVIF / JPEG XR export

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
