# Changelog

All notable changes to Snapture will be documented in this file.

### Added — Plugin capture source contract

- Added `ICaptureSource` for camera, scanner, file-watch, virtual-device, and other local plugin sources, plus a shared invocation boundary that returns metadata by default and copies pixels only when requested.

### Added — Plugin SDK package build

- `build/build.ps1 -NuGet` now emits and validates the multi-target `Snapture.Plugin.Abstractions` package and symbols, including the project README, MIT metadata, and `netstandard2.0` / `net10.0` assemblies. No nuget.org credentials are read by the build.

### Changed — Plugin capability consent

- Plugin capability declarations are now enforced at load and install time. Network, filesystem, clipboard, process-launch, and UI-interaction requests are shown for explicit user approval and approvals are versioned with the manifest; unapproved or manually dropped capability plugins remain unloaded.

### Added — Encrypted plugin secrets

- Added the optional `IPluginSecretStore` extension for uploader and destination plugins. Each plugin gets an identity-scoped atomic store protected with Windows DPAPI (current-user scope), including portable-mode data roots, with no plaintext credential fallback.

### Added — Plugin manager workflow

- The Plugins window can install/update a selected DLL, uninstall a loaded plugin, and open a host-rendered JSON configuration editor for plugins implementing `IPluginConfigurable`.
- Install/update is staged through a temporary file, validates the plugin manifest and host-version range before loading, and restores the prior DLL if activation fails.

### Changed — Plugin host compatibility guard

- `[SnapturePlugin]` now accepts inclusive `minHostVersion` and `maxHostVersion` bounds. Snapture rejects invalid, reversed, or out-of-range manifests before instantiating plugin contracts and surfaces the declared range in the Plugins window.

### Added — Portable mode

- `--portable` is now a startup modifier for GUI, CLI, URI, and conversion flows; portable archives include a colocated `Snapture.ini` marker and keep settings, history, plugins, logs, autosave, crash data, and temporary capture artifacts under `SnaptureData` beside the executable.
- Added portable-mode path coverage and archive verification while keeping installed Velopack packages on the normal per-user data roots.

### Added — Opt-in local MCP server

- Added a loopback-only Streamable HTTP MCP endpoint at `/mcp` with metadata-first tools for region, window, monitor, UI Automation element, scrolling capture, local OCR, history search, window/monitor discovery, and Auto-redact.
- Added Settings → Integrations controls with MCP disabled by default, a configurable localhost port, explicit start/stop, strict Origin validation, path boundaries, concurrency limits, and opt-in full PNG responses.

### Added — Enterprise MSI packaging

- `build/build.ps1 -Msi` now emits unsigned per-machine x64 and ARM64 MSIs with Add/Remove Programs registration, a machine-wide Start Menu shortcut, major-upgrade handling, and silent `msiexec` deployment support.
- Each MSI build also emits a WiX-generated enterprise MST transform that changes the shortcut name to `Snapture Enterprise`; the build validates both MSI databases and the transform table mutation.

### Added — Scoop manifest

- Added an extras-bucket-ready `packaging/scoop/snapture.json` manifest for the x64 and ARM64 portable archives, including pinned SHA-256 hashes, Scoop shims, a Start Menu shortcut, GitHub version checking, and architecture-aware autoupdate URLs.

### Added — Chocolatey packages

- `build/build.ps1 -Chocolatey` now builds both x64 and ARM64 Velopack release assets and emits `snapture` and `snapture.portable` packages with architecture-aware GitHub download URLs, SHA-256 verification, silent installer handling, portable extraction, shims, and uninstall scripts.

### Added — Velopack auto-updates

- Installed builds now use the pinned Velopack 1.2.0 architecture-specific stable channel to check, download, and apply GitHub Release updates from the tray, with an explicit restart prompt.
- `build/build.ps1 -Velopack` produces unsigned `win-x64-stable` and `win-arm64-stable` assets, including each feed, full package, setup executable, and portable archive. Unpackaged source builds retain the existing GitHub release-page fallback.

### Added — MSIX packaging path

- `build/build.ps1 -Msix` publishes and validates an unsigned MSIX with `runFullTrust`, the Windows startup-task extension, no broad file-system capability, generated tile assets, and a pinned App Installer feed for canary/pilot/stable rollout rings.
- `build/uninstall.ps1` removes current-user registrations and local Snapture state, with a Keep-settings checkbox plus quiet, keep-settings, and dry-run switches.

### Added — Per-app capture profiles

- Settings → Output can map case-insensitive Win32 foreground window class names to the existing Bug-report, Code-block, Documentation, or Quick-share-LAN presets. Matching profiles apply automatically before capture and persist with settings import/export.

### Added — Headless CLI capture

- `Snapture.App.exe` now accepts fixed-region or fullscreen capture commands with explicit output paths, local capture profiles, optional clipboard copy, bounded holds, and LAN-share registration without starting the tray or editor.
- The CLI also supports `--open` for editor handoff and local `--convert` / `--resize` commands for Explorer integration.

### Added — Explorer image integration

- Tray → Tools can install or remove a current-user-only image context menu with “Open in Snapture editor”, PNG/JPEG conversion, and 50/75/125/200% resize presets. Registration is stored below `HKCU\Software\Classes\SystemFileAssociations\image` and never requires elevation.

### Added — Secure URL capture handler

- Tray → Tools can opt in to a current-user `snapture://` protocol registration. Region, window, fullscreen, scrolling, and last-region commands dispatch through the existing capture pipeline with clipboard/editor destination overrides.
- The parser rejects UNC, SMB, external `file://`, traversal, outside-profile paths, credentials, ports, fragments, duplicates, and unknown parameters before any capture work, with explicit reject-test coverage for the CVE-2026-33829 failure class.

### Added — Windows 11 Jump List capture verbs

- Snapture registers taskbar entries for New region, New window, and New fullscreen. Each verb routes through the normal app host and capture delivery pipeline.

### Added — Multi-pin selection and group controls

- Pin overlays support Ctrl+click selection, Ctrl+A select-all, group dragging, group opacity changes through the wheel or context menu, and Delete/context-menu bulk close. Selected pins use an accent border and the menu reports the active group.

### Added — Capture-safe desktop icon hiding

- Settings → Capture engine can temporarily hide the Windows desktop icon list before capture and restores the prior visibility state on success, cancellation, or failure.

### Added — LAN-share QR overlay

- Editor → Share to LAN now opens a scan-ready QR window for the existing single-fetch URL while keeping the URL in the clipboard. QR images are generated locally with the same TTL and network-boundary rules as the share URL.

### Added — Markdown clipboard integration

- Settings → Output can keep image clipboard behavior or switch automatic copies to a relative Markdown link. Snapture writes a PNG under the configured vault/export folder, discovers the active Obsidian vault when possible, and provides Ctrl+Alt+V plus a tray action to pin the most recent capture. Joplin users can select a Markdown export or attachment folder because live Joplin resources are managed internally.

### Added — Daemon-safe plugin metadata responses

- External processor callers now have a shared invocation boundary that returns dimensions, stride, SHA-256 pixel hash, capture source, timestamp, and optional saved path by default. Processed pixel buffers require an explicit opt-in response mode, while the normal capture flow continues to retain pixels in-process.

### Changed — Shared RapidOCR redaction path

- Auto-redact now tries the bundled RapidOCR DBNet text-region pipeline first and turns that normalized word geometry directly into findings, so OCR and redaction share one local ONNX pass before the general OCR fallback chain.

### Added — Local AI capture analysis

- The editor can flatten the current capture to a base64 PNG, let the user choose a discovered `provider/model`, and send it to a local OpenAI-compatible runtime. Results remain in a local response window; cloud endpoints are excluded.

### Added — Local AI provider discovery

- Settings now probes Foundry Local, Ollama, and LM Studio on loopback-only endpoints and shows their locally available models with `provider/model` references. Cloud providers are intentionally absent.

### Added — WinAppSDK storage pickers

- File and folder selection now uses the Windows App SDK 1.8 `Microsoft.Windows.Storage.Pickers` APIs for WPF windows, with an owner-aware Win32 fallback for tray-only flows and runtime compatibility.

### Added — Optional OneOCR sidecar

- OCR now discovers or accepts a user-supplied `sponeocr.exe` sidecar that follows the community OneOCR pattern (`stdin stdout`). Images remain local, the process is launched without a shell, and bounded timeout/output guards fail back cleanly to RapidOCR.
- The Windows Snipping Tool DLLs and `.onemodel` are intentionally not redistributed. Settings exposes the optional executable path and reports whether the sidecar is available.

### Added — Step Capture Office exports

- Step Capture now exports editable Word documents and PowerPoint presentations with a title slide, editable captions, and embedded step images alongside the existing Markdown bundle.

### Added — Step Capture input tracks

- Step Capture now records key chords and cursor click coordinates/buttons with each screenshot, shows the track during review, and includes it in Markdown, DOCX, and PPTX exports.

### Added — History image features

- History now stores a local dominant-color signature and 64-bit perceptual hash for each capture, supports color-similarity and near-duplicate filters, and shows the feature metadata in the thumbnail wall.

### Added — History projects

- History now supports named local projects, project filtering, and extended multi-select assignment without moving the original image files. Existing captures remain in the unassigned Inbox until organized.

### Added — History library backup

- History can now export the SQLite index, projects, and referenced capture images into one `.snapture-library` archive and restore it on a fresh install. Imports validate archive paths, preserve project assignments, skip duplicate captures, and keep existing files untouched.

### Added — Verified-redacted history marker

- Auto-redact results are marked `Verified-redacted` only after a successful image export, can be filtered in History, and are cleared if the saved file is later exported without those active redaction shapes. Restored libraries preserve the marker.

### Added — Capture presets

- Settings and the tray now expose Bug-report, Code-block, Documentation, and Quick-share-LAN templates. Each preset applies a deterministic local output folder, filename pattern, format, cursor/editor behavior, and LAN-share toggle while leaving the fields editable before saving.

### Added — OCR text overlays

- The editor can now turn positioned OCR lines into editable `TextShape` annotations. Each overlay is anchored to its source image region, adapts between dark/light text for contrast, and lands as one undoable batch; text-only OCR engines report that positions are unavailable instead of guessing.

### Added — OCR table mode

- Editor → Table reconstructs rows and columns from positioned OCR word boxes and offers a TSV copy action. Text-only OCR engines report that table geometry is unavailable.

### Added — QR and barcode extraction

- Editor → Codes scans captures locally with ZXing.Net, reports supported QR/barcode formats and image regions in a dedicated result window, and offers one-click payload copying. No network request is made.

## [unreleased] — recording wave

### Added — Recording quality presets and output resolution

- Recording quality presets: Low (2 Mbps, 20 fps), Medium (5 Mbps, 30 fps), High (8 Mbps, 30 fps), Ultra (16 Mbps, 60 fps). Stored in settings, selectable via tray menu Record Video submenu.
- Recording output resolution presets: Native, 720p, 1080p, 1440p, 4K, 9:16, 1:1. The encoder scales source frames to the target resolution via Media Foundation's automatic video processor insertion.
- Recording window now shows source-to-output resolution in the format text when a non-native preset is active.
- Recording now handles environment changes: capture source window/monitor close auto-pauses recording (with "SOURCE LOST" status); system suspend pauses and resume resumes; display settings changes (DPI, topology) force the next frame to re-encode.
- Zero-size frames from GPU switches or topology changes are silently dropped instead of crashing the encoder.
- Editor Select tool now shows 8 transform handles (4 corners + 4 edge midpoints) around single-selected shapes. Drag any handle to resize; drag the shape itself to move. All 12 shape types support `ResizeTo` with proportional point remapping for line/arrow/freehand shapes.
- Shift+Retake in the editor refreshes the capture background while preserving all annotations (Snipaste Pro parity).
- Filename pattern now supports `{MonitorIndex}`, `{MonitorDpi}`, and `{HDR}` tokens.
- Tray menu cursor inclusion toggle for quick per-session override.
- User-triggered update check via GitHub releases API (tray menu).
- Local crash dump files written to `%LOCALAPPDATA%\Snapture\crashes\` for user-initiated bug reports.
- Windows high-contrast mode detection drops custom palette, letting system colors take over.
- Reduced-motion preference honored: capture flash and recording dot animations suppressed when Windows "Show animations" is off.
- UIA automation root pre-warmed on startup to eliminate Smart Capture first-attach lag.
- One-shot "engine upgraded to WinRT" toast on first WinRT activation.
- Redact rule-pack version (2026.1, 30+ rules) shown in About box and diagnostic dump.
- Dependabot CVE watch for all NuGet dependencies.
- xUnit test suite with 9 green tests covering ImageStitcher and MonitorEnumerator.

### Added — Documentation

- `docs/ARCHITECTURE.md` — engine seams, threading model, dependency map.
- `docs/PLUGINS.md` — interface reference, capability manifest, credential guidance.
- `docs/INSTALL.md` — ZIP, source, winget, portable instructions.
- `docs/AI-LOCAL.md` — local-AI carve-out and no-cloud anchor.
- `docs/PRIVACY.md` — GDPR, HIPAA, US-state compliance posture added.
- README auto-redact competitive line and GitHub downloads badge.

### Added — Recording codec fallback

- MP4 recording now discovers Media Foundation video encoders at runtime and tries AV1 hardware, HEVC hardware/software, then H.264 hardware/software. Software AV1 is intentionally excluded because it is not viable for live screen recording.
- Codec discovery logs detected NVIDIA NVENC / Intel QSV / AMD VCN / Qualcomm / generic Media Foundation encoders and surfaces the selected codec in the recording window.
- MP4 recording now enables WinRT dirty-region reporting when the OS exposes it, writes the first frame, then skips later frames that report no dirty rectangles. The recording window shows skipped clean-frame count and whether dirty-region skip is active.
- MP4 recording now writes fragmented MP4 (`moof`/`mdat`) by default with two-second fragments so recordings have crash-recoverable partial media if Snapture is killed mid-record.
- MP4 recording now captures a mixed AAC audio track from WASAPI loopback system audio and microphone input, with selectable sources and live VU meters in the recording window.
- Foreground-window MP4 recording can now switch system audio to app-only process-tree loopback, excluding other system audio while preserving mic mixing and level monitoring.
- MP4 recording now overlays a cursor highlight plus fading click rings directly into encoded frames, and mixes a short synthesized click sound into the AAC track for in-bounds clicks.
- MP4 recording now adds a visible keystroke overlay track with shortcut chords, repeat counts, and fade-out timing; generated overlay activity forces frames even when WGC dirty-region reporting marks the desktop clean.

### Added — Cursor auto-zoom suggestions

- Video recordings can collect cursor telemetry and infer click-focused zoom segments, merging nearby clicks and clamping crops to the recorded frame. The recording HUD exposes the opt-in toggle, and saved recordings receive a `.snapture-zoom.json` sidecar with versioned, editor-friendly crop metadata.

### Added — Tabbed editor

- Capture, history, dropped-file, and autosave-recovery flows now reuse one tabbed editor host. Each tab retains independent undo, autosave, annotation, and export state, with accessible close actions.

### Added — Modern curved arrows

- Arrow annotations now support a backward-compatible Classic filled head or Modern rounded open chevron, plus signed quadratic curves with matching tangent-aware heads. The Arrow tool exposes style and curve controls, and both settings round-trip through `.snapture` projects.

### Added — Vertical text

- Text annotations now support horizontal or vertical top-to-bottom orientation. The Text tool exposes the direction selector, and orientation survives cloning and `.snapture` project round-trips.

### Added — Speech balloons

- The editor now includes a Speech balloon tool with a tail, adjustable 0–64px corner radius, drag/resize/hit-test behavior, and `.snapture` persistence.

### Added — Annotation categories

- Annotations can now be tagged None, Blocker, Question, or Nit. Category changes apply to selected shapes through undo/redo, persist in `.snapture` files and clones, and render as compact color-coded badges.

### Added — Crop workflow

- The editor Crop tool now shows a snap-aware selection overlay, crops the background and intersecting annotations as one undoable operation, drops annotations fully outside the crop, and refreshes the canvas dimensions after apply. Escape cancels an in-progress selection.

### Added — Full hand-drawn styling

- The deterministic sloppiness slider now styles rounded rectangles, speech balloons, spotlight cutouts, arrowheads, highlights, blur edges, redaction edges, step markers, ruler details, and text placement in addition to the existing line and freehand geometry. Redaction coverage remains a complete solid fill.

### Added — Spacebar options panel toggle

- The editor now exposes an accessible Options button and a Spacebar toggle for the side panel. Hiding it collapses the panel column so the canvas expands, while focused buttons, sliders, checkboxes, and text controls retain their normal Spacebar behavior.

### Added — Code line-state markers

- The editor now has a Code line marker tool with Added, Removed, Focus, Blur, and Fade states. Markers persist in `.snapture` projects, render over captured code lines, and carry through Carbon code-window and beautifier exports.

### Added — Windows AI OCR with normalized fallback

- OCR now attempts the Windows AI Foundry `TextRecognizer` on Windows 11 24H2+ when its local model and runtime are ready, exposing engine identity, per-word confidence, quadrilateral polygons, and rectangular bounds.
- Devices without the Windows AI COM surface, NPU model, or required runtime transparently fall back to `Windows.Media.Ocr`; Auto-redact consumes the same normalized result contract from either engine.

### Added — RapidOCR fallback and provider controls

- OCR now falls through to bundled RapidOcrNet PP-OCRv5 Latin models when neither Windows OCR engine can return text, preserving the shared line, word-confidence, rectangle, and polygon contract for Auto-redact and result views.
- ONNX Runtime is explicitly pinned to 1.27.1 and SkiaSharp is upgraded to 3.119.2 for the maintained RapidOcrNet 3.0.0 package; the editor's text rendering now uses the SkiaSharp 3 font/sampling APIs with a clean build.
- Settings exposes an opt-in DirectML device-0 toggle for RapidOCR. If the optional native provider is absent or cannot initialize the models, OCR reports the CPU fallback and continues locally without failing the capture flow.

### Added — Mermaid and PlantUML diagrams

- The editor now accepts Mermaid `flowchart`/`graph` and PlantUML `@startuml` snippets from the clipboard or a multiline paste dialog via the toolbar or Ctrl+Shift+V.
- Imported diagrams become one editable vector shape with selectable nodes, connectors, labels, hand-drawn sloppiness, undo/redo, `.snapture` persistence, and PNG/SVG/beautifier export support.

### Added — UIA-driven recording auto-tighten

- An opt-in recording setting detects edge-mounted tabs, toolbars, menus, status bars, docks, and Windows taskbars through read-only UI Automation. Safe plans crop those strips before encoding, remap cursor effects into the cropped frame, and leave the full capture untouched when the remaining content would be too small or UIA is unavailable.

### Added — Video trim and segment split

- The Tools → Record MP4 video menu can open a recording in a local trim/split window. Trim and split operations render new MP4 files through Windows Media Composition, preserve the source, reject invalid or sub-frame ranges, and expose clear errors when the OS codec cannot render the input.

### Added — Ring-buffer recording

- Tools → Record MP4 video → Ring buffer can continuously retain a bounded foreground-window or primary-monitor recording and save the last 30, 60, or 90 seconds on demand. The rolling source rotates at 90 seconds, stays in the Snapture temp area, and is deleted after save, stop, or shutdown.

### Added — GIF frame editor

- Stopping a GIF recording now opens a frame-by-frame editor with thumbnail previews, delete / duplicate operations, per-frame delay editing, ordered Bayer dithering, and non-destructive GIF export.
- Existing GIFs can be opened from Tools → Edit GIF; deletion-only edits expose a lossless clip-save action that copies the original GIF frame blocks without re-encoding.

### Changed — Magick.NET GIF output

- GIF exports now use the pinned Magick.NET-Q8-x64 14.15.0 pipeline with a shared 256-color palette, centisecond frame timing, and a bounded 1% ColorFuzz layer optimization for smaller lossy GIFs.

### Added — Animated modern image output

- The frame editor can now save animated PNG (`.apng`) and animated AVIF (`.avif`) files with per-frame timing, while retaining GIF as the default and reporting unavailable native delegates clearly.

### Added — HDR capture pipeline

- WinRT monitor and window capture now queries the target DXGI output's current color space and selects an FP16 (`R16G16B16A16_FLOAT`) frame pool only for HDR/PQ Rec. 2020 output. FP16 surfaces are tone-mapped to the existing BGRA8 boundary, with deterministic BGRA8 fallback when the monitor query or WGC pool is unavailable.

- HDR tone mapping is selectable in Settings → Capture — Reinhard (default), ACES, or Hable — and the choice applies to still captures, MP4 recordings, and the rolling ring buffer.

- HDR captures now write a PNG primary plus JPEG XL archival and AVIF sharing siblings automatically; optional WIC JXR output is exposed for Game Bar compatibility and is documented as SDR-clamped.
- Settings now flags suspiciously low HDR peak luminance and deep-links to Windows HDR calibration before capture.
- Video recording now probes Media Foundation AV1/HEVC/H.264 encoders and the WIC HEIF encoder, reports missing Store codec extensions, and falls back to the next available encoder.
- HDR capture and recording now expose an HDR screenshot color-correction toggle; disabling it uses a direct scRGB clamp and clearly surfaces possible highlight clipping.
- PNG saves now embed the active single-monitor ICC profile as a compressed `iCCP` chunk when WCS exposes one; multi-monitor composites and JPEG output remain untagged.
- Screen and monitor captures now detect visible layered or tool-topmost overlays and route those rectangles through a short-lived STA Magnification API helper when WGC/GDI would omit the composed overlay; ordinary captures keep their existing fast path.
- Scrolling capture now recognizes Chromium-family windows, searches the default UIA tree for the largest visible document scroll provider, and falls back from `LargeIncrement` to a viewport-percent step when Chromium does not advance the provider.
- Scrolling capture now waits for lazy-loaded content to settle at each scroll position with bounded sampled-frame comparisons; animated pages remain bounded and use their newest frame.
- Scrolling capture now shows a non-modal live viewport preview with frame count and scroll progress while UIA drives the source window; the preview closes before the stitched result is delivered.

### Added — ARM64 distribution

- ARM64 publishing now selects the native `Magick.NET-Q8-arm64` package while x64 keeps the existing native package; both runtime-specific publish paths produce architecture-matched hosts and ZIP artifacts.

### Added — Canvas colour wheel

- Right-clicking the editor canvas opens a vector-rendered colour wheel; choosing a colour updates the active drawing colour and recolours the shape under the pointer (or the selected group) as one undoable action.

### Added — Hand-drawn stroke styling

- The editor now exposes a 0–100% sloppiness slider for new annotations, storing a deterministic roughness amount in each shape and applying crisp, repeatable wobble to stroke, rectangle, ellipse, and freehand geometry.

### Fixed

- Video recording now crops odd-width or odd-height WGC textures into even encoder dimensions without attempting an invalid `CopyResource` between differently sized D3D11 textures.

### Changed — Premium desktop UX polish

- Shared WPF chrome now has a tighter premium baseline: restrained header/footer bars, section panels, consistent button/input/toggle sizing, darker list selection states, and custom dark progress, slider, and scrollbar treatments.
- Editor, Settings, History, Plugins, Step Capture, OCR, capture picker, color picker, and recording HUD windows were rebalanced around clearer hierarchy, calmer copy, stronger grouping, and more predictable primary/secondary actions.
- Empty, unavailable, and degraded states are now explicit on history thumbnails, plugin loading, Step Capture, and OCR results instead of relying on blank surfaces or placeholder text.
- Accessibility and interaction polish improved across tool buttons, color swatches, capture-picker options, selected tool states, disabled actions, focus-visible controls, and screen-reader names/help text.
- The region capture loupe now uses the same restrained radius system as the rest of the app chrome.

### Changed — Views switched to semantic theme tokens

- All thirteen view XAMLs and their code-behinds now bind colors via the `App*` semantic tokens (`AppBackground`, `AppSurface`, `AppCanvas`, `AppBorder`, `AppBorderStrong`, `AppForeground`, `AppMutedForeground`, `AppSubtleForeground`, `AppAccent`, `AppWarning`) instead of the legacy Catppuccin palette names (`Base`, `Mantle`, `Crust`, `Surface0/1/2`, `Text`, `Subtext`, `Overlay0/1`, `Mauve`, `Accent`). Both palettes still export the legacy names for plugin-API compatibility, but Snapture's own surfaces no longer depend on a specific palette flavor — the same XAML reads correctly on Mocha (dark) and Latte (light). Two stale palette references in `SettingsWindow.xaml.cs` (the redact-rule list builder) and the tray theme menu's `IsChecked` comparison (now normalized via `ThemeManager.NormalizeMode`) were also fixed.
- Tray About box pulls the version from the assembly instead of the hardcoded `v0.2.0` string and surfaces the active theme + effective mode for diagnostics.

## [v0.6.0] — 2026-05-08

The "more polish, less repeat work" pass. Sticky-strip detection, animated GIF recording, per-rule auto-redact toggles, plugin-resize widening, and the docs / distribution items that round out a serious release.

### Added — Sticky-header / sticky-footer detection (v0.6.1)

- `ImageStitcher.DetectStickyStrips` finds rows from the top and bottom that are pixelwise stable across every frame (per-row mean-absolute-difference threshold of 8 against frame[0], capped at 240 source pixels). Sticky bars are emitted exactly once at the top and bottom of the stitched output.
- `FindOverlap` now restricts the strip-search to the body region (sticky stripped off both ends) so navbars don't anchor the alignment to a wrong row.
- Honest residuals: animation inside the sticky bar (clocks, carousels) breaks the row stability check and reverts to per-frame repeat.

### Added — GIF recording (v0.6.4)

- `Services/GifRecorder` captures the foreground window or the full virtual screen on a fixed cadence (default 10 fps), keeps frames in memory while recording, and encodes animated GIF output on stop. Frame delay is configurable.
- `Views/GifRecordingWindow` — small always-on-top REC-indicator parked top-right of the work area; live frame-count + elapsed timer; Stop & save / Discard.
- Tray menu **Tools → Record GIF** with two entries: …of foreground window · …of all monitors.
- Animated GIF output is saved through a standard SaveFileDialog; reveals in Explorer on success.

### Added — Per-rule auto-redact toggles (v0.6.2)

- New Settings tab **Auto-redact** lists every `SecretDetector.Rules` entry with a checkbox, plus Enable all / Disable all shortcut buttons.
- Persisted as `DisabledRedactRules: string[]` in `settings.json`. New rules in future releases ship enabled for everyone — only the disabled set travels.
- `SecretDetector.Scan` and `AutoRedactor.ScanAsync` accept an optional `disabledRuleIds` set. The editor's Auto-redact button reads from settings.

### Added — Plugin contract widening (v0.6.3)

- `ICaptureProcessor.ProcessAsync` may now return a `PluginCapture` of different dimensions than the input. `CaptureOrchestrator.ApplyPluginCaptureBack` constructs a fresh `Bitmap` and `CaptureResult` when the size changes, honouring the plugin's reported `Stride`.
- Plugin processors now run **before** the on-disk save so resize / watermark / redact lands in the saved file and the history index. Order: capture → plugin processors → save → history → clipboard → editor.

### Added — Docs & distribution (v0.6.5)

- [`docs/HOTKEYS.md`](docs/HOTKEYS.md) — canonical reference for every hotkey across the global / tray / editor / pin / region / window-picker / Smart-capture / color-picker / ruler / Step-Capture surfaces.
- [`docs/CAPTURE-MATRIX.md`](docs/CAPTURE-MATRIX.md) — engines table, per-Windows-build capability matrix, capture-mode × engine results, WGC limitations, cursor handling, DPI awareness, per-release verification matrix.
- [`manifests/SysAdminDoc/Snapture/0.6.0/`](manifests/SysAdminDoc/Snapture/0.6.0/) — winget multi-file manifest set targeting schema 1.7.0 (version + installer + en-US locale). Portable-ZIP installer pointing at the GitHub release asset; SHA-256 placeholder for the submitter to fill in.
- [`manifests/README.md`](manifests/README.md) — submission instructions for `microsoft/winget-pkgs`.

### Changed

- All version strings synced to 0.6.0 across `Snapture.App` / `Snapture.Capture` / `Snapture.Plugin.Abstractions` csproj files, README badge, and the workflow default.
- Tray Tools submenu now: Color picker · Pixel ruler · OCR region · Record GIF (window / all monitors) · Step Capture · Plugins · Capture history.
- Theme infrastructure now supports persisted System / Light / Dark modes with Catppuccin Mocha and Latte palettes, shared semantic WPF control styles, and tray/settings theme switching.

### Architecture notes

- The sticky-strip detector reads a downsampled gray-luminance buffer for every frame's top and bottom region (`probeH = min(MaxStickyRows, height/3)`). For 10 frames at 1920×1080 with subsample factor 4, that's ~10 × 240 × 480 = ~1.15 MB of grayscale buffers — fits in L2.
- `GifRecorder` keeps captured `Bitmap` instances in a `lock`-protected list so the UI thread can read frame counts safely. The capture loop runs on `Task.Run`; encoder runs synchronously on Stop.
- The plugin resize path validates only `Width * Height` against the input. Stride mismatches (a plugin that returned tightly-packed rows from a resized source) are honoured by per-row `Marshal.Copy` rather than buffer-length checks.

### Deferred to v0.7

- MP4 / HEVC / AV1 recording (Media Foundation `SinkWriter` + hardware-encode discovery)
- HDR tonemap (ACES via Win2D) + AVIF / JPEG XR export
- RapidOCR ONNX bundle (model download flow)
- DOCX / PPTX export from Step Capture
- MSIX manifest (gated on SignPath OSS approval)
- Magnification API fallback for layered overlays
- Full transform handles for the Select editor tool
- Right-click colour wheel / sloppiness slider / refresh-capture-preserving-annotations

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
