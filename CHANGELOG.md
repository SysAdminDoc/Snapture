# Changelog

All notable changes to Snapture will be documented in this file.

## [Unreleased]

## [v0.8.1] — 2026-08-09

### Maintenance

- Synchronized application, capture library, plugin SDK, README, and distribution metadata for the v0.8.1 roadmap-drain release. The About dialog continues to report the assembly version dynamically.

## [v0.8.0] — 2026-08-08

### Security

- The opt-in loopback MCP endpoint now requires a cryptographically random in-memory bearer token that rotates on every server start; the Settings integration surface shows the token only while the server is running.
- File-backed image intake now uses one bounded, content-aware contract across watch-folder, conversion, editor, MCP, batch, GIF, clipboard, Office, and upload paths, rejecting signature mismatches, reparse paths, decoder failures, oversized dimensions, and pixel bombs before history insertion.
- Network and child-process boundaries now have an executable data-flow inventory; upload actions preview the destination and payload, reject embedded URL credentials, stop built-in redirects, and redact sensitive headers and transport errors from visible diagnostics and logs.
- Plugin installation now requires separate approval for the exact DLL SHA-256 artifact and declared capabilities; hash/version changes invalidate trust, rollback restores the previous artifact on failed activation, and the UI states that in-process loading is not an OS sandbox.
- Release verification now scans the x64 and ARM64 payloads offline, emits deterministic CycloneDX SBOMs with license and native-component inventories, binds each SBOM to a complete artifact manifest hash, and enforces SQLite, ImageMagick, Windows App SDK, ONNX Runtime, SkiaSharp, native codec, and .NET runtime floors.

### Reliability

- Ring-buffer recording now writes an atomic per-session manifest and bounded 30-second fragmented-MP4 segments, keeps the last 90 seconds across segment boundaries, detects interrupted/corrupt/stale sessions on restart, quarantines recoverable media for explicit tray review, and retains failed-save sources instead of deleting them.

### Local AI

- Local-AI discovery now carries provider protocol, vision capability, model identity, image/request/response budgets, prompt limits, and timeout metadata; inference validates decodable PNGs before base64 expansion, reads responses with a hard cap, keeps all requests loopback-only, and classifies unavailable models, non-vision models, provider failures, invalid/oversized responses, cancellation, and timeouts for offline tests and user diagnostics.

### Export metadata

- Export settings now make ordinary source metadata, ICC profiles, and provenance separate decisions. PNG/JPEG/WebP capture, editor, HDR, and headless conversion paths support strip/preserve/replace policies, redaction suppression, explicit composite ICC notices, and an opt-in inspectable `.provenance.json` sidecar that makes no C2PA authenticity claim.

### Documentation and release checks

- Corrected the install, plugin, architecture, privacy, and README release claims for shipped portable/ARM64 packages, current-user DPAPI secrets, current package versions, executable network boundaries, and released v0.7 recording.
- Added the offline `build/verify-docs.ps1` drift gate with version/package extraction, CLI/privacy/architecture checks, and a self-test covering known stale-claim cases without creating Markdown artifacts.

## [v0.7.0] — 2026-08-03

### Changed

- The release wave adds bounded local image utilities, comparison workflows, edge measurement, pinned boards, and horizontal/two-axis scrolling while preserving the existing vertical capture path.

### Fixed

- Clean release builds now resolve the bundled SQLite native library from SQLitePCLRaw 3.0.5 / SQLite 3.53.4, above the roadmap security floor.

### Security

- The dependency floor audit tracks SQLite advisories CVE-2025-6965 and CVE-2025-29088; the shipped native SQLite 3.53.4 runtime is above the vulnerable floor.

### Added — Omnidirectional scrolling capture

- Tray capture now exposes horizontal and omnidirectional UIA scrolling modes. Captured tiles are placed from both scroll axes into a bounded canvas, with the existing vertical mode unchanged.

### Added — Pinned comparison boards

- Selected pins can open a local comparison board with vertical, horizontal, or grid snap-arrangement, bounded spacing/columns, and named layout presets stored without copying image pixels.

### Added — Edge-detection ruler

- Pixel ruler now supports Alt+click nearest-edge measurement against a frozen local screen sample, reporting the closest high-contrast edge and contrast score without changing the normal drag ruler.

### Added — Code-aware capture export

- Tray → Tools → Code-aware export runs local OCR, scores code and monospace signals, and exports syntax-highlighted code text with the existing gradient, drop-shadow, and code-window chrome.

### Added — Before/after comparison GIFs

- Tray → Tools → Before/after GIF creates a local ping-pong cross-fade animation from two still images, with bounded transition frames and frame delay.

### Added — Image combiner

- Tray → Tools → Combine images combines two to 100 local stills vertically, horizontally, or in a configurable grid with bounded gaps, canvas size, and PNG/JPG/BMP/WebP export.

### Added — Local batch image processing

- Tray → Tools → Batch process images applies bounded resize, border, watermark, and format conversion effects to a selected local folder, with per-file results and no automatic uploads.

### Security — Hostile project and plugin input boundaries

- `.snapture` loading now rejects malformed ZIPs, unsupported or duplicate entries, oversized payloads, oversized backgrounds, and invalid document JSON before exposing data to the editor; fuzz coverage asserts clean rejection and no path traversal.
- Plugin loader tests cover malformed DLLs, denied capability manifests, constructor failures, cancellation of a non-returning processor, and collectible load contexts. Test loaders can use an injected plugin directory so adversarial fixtures never touch user data.

### Added — Opt-in watch-folder imports

- Settings → Output can watch a selected local folder and add completed PNG/JPEG/BMP/GIF/WebP/TIFF files to History. The watcher waits for stable size/timestamps, ignores unsupported files, and avoids duplicate event imports; it is disabled by default.

### Added — Resource-backed localization foundation

- Added an embedded English `.resx` catalog with deterministic SHA-256 resource keys, culture selection, safe fallback for plugin-supplied copy, and a WPF load hook that localizes window titles, controls, tooltips, headers, and accessibility names.
- Tray notifications use the same catalog, and the explicit Phase-1 culture list is ready for reviewed satellite resources without changing view code.

### Added — On-demand plugin dependency cache

- Added `IPluginDependencyStore` / `PluginDependency` to the SDK. Plugins can request pinned HTTPS tools such as ffmpeg or Tesseract only when a feature is used; the host downloads into a per-plugin cache, enforces a 500 MB cap, verifies SHA-256, and atomically exposes the path.
- Dependency downloads are never performed during plugin discovery, and malformed URLs, file names, versions, or hashes are rejected before network access.

### Added — Opt-in self-hosted destinations

- Added built-in Nextcloud WebDAV and Immich asset/album destinations. Both are disabled by default, require an explicit upload action, and are verified through mocked HTTP contracts.
- Nextcloud app passwords and Immich API keys are stored in the existing current-user DPAPI secret store rather than `settings.json`; no external server or credential is bundled.

### Added — Declarative uploader profiles

- Added ShareX-compatible `.sxcu` / JSON import for user-owned HTTP uploaders, including multipart, form-url-encoded, JSON, XML, binary, query, header, and response JSON-path handling.
- Imported uploaders remain inert until an explicit editor or tray action and enforce bounded payloads, response sizes, timeouts, and no implicit destination changes.

### Added — External command destination

- Settings → Output can store explicit local CLI profiles that receive a flattened editor PNG through stdin or a temporary `{file}` path argument, with metadata placeholders, timeout bounds, captured output, and direct no-shell process execution.
- The editor and tray Tools menu can run a selected profile on the current or latest capture. No command runs automatically and no executable is bundled.

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

- Tools → Record MP4 video → Ring buffer can continuously retain a bounded foreground-window or primary-monitor recording and save the last 30, 60, or 90 seconds on demand. The source uses three 30-second segments in an atomic per-session manifest, stays in the Snapture temp area, and is discarded on an explicit stop; interrupted recoverable sessions are quarantined for explicit tray review.

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

## Roadmap archive — 2026-08-10 — ROADMAP.md

<details>
<summary>Original roadmap snapshot</summary>

```markdown
# Snapture Roadmap

**Version:** 2026-05-17 (post-v0.6 + 9-day delta refresh) · **Tracks:** v0.1.0 → v1.0
**Build philosophy:** WinRT-first · No cloud · No telemetry · Local-first as a feature, not an ideology footnote · Polish that beats Snagit [S2 §1] · Knobs that beat ShareX [S1] · Modern AI surface (Copilot+ NPU when present) without ever leaving the device [S6 platform].

Items use `[ ]` (open) / `[x]` (shipped). Each line carries a source bracket like `[S3]` or `[S6]` mapping to the Appendix. Tiers: **Shipped** → **Now (v0.7)** → **Next (v0.8)** → **Later (v0.9 / v1.0)** → **Stretch** → **Rejected** (with reasons). Cross-cutting tracks (Security, Accessibility, i18n, Observability, Testing, Performance/Power, Compliance, Docs, Distribution, Migration) live at the bottom and run alongside every release.

**Delta since 2026-05-08 (this refresh):** May Patch Tuesday 2026-05-12 landed four .NET CVEs (rolled into 10.0.8) plus two Windows graphics RCEs (CVE-2026-40403 Win32K-GFX, CVE-2026-35421 GDI EMF) — both touch Snapture fallback paths [S6+9d sec]. WindowsAppSDK 1.8.8 shipped same day with three fixes that hit our editor preview + Windows AI gating [S6+9d platform]. 26H1 Release Preview hit 28000.2173 (KB5089570) with NPU task management improvements [S6+9d platform]. Snipping Tool gained "Perfect Screenshot" (AI auto-crop) + native color picker in the Insider channel, narrowing the v0.7 differentiator window; "Ask Copilot" was concurrently removed from Snipping Tool + Photos [S6+9d community]. Peekaboo v3.0→v3.2.0 landed pluggable local-model syntax (`ollama/<model>`, `lmstudio/<model>`) — directly applicable to v0.8.6 AI carve-out [S6+9d OSS]. Screenity v4.4.0→v4.4.7 pivoted recording to WebCodecs + OPFS direct-to-disk — architecture reference for v0.7.1 streaming [S6+9d OSS]. Raycast 2.0 hit Windows public beta (May 14) and started indexing screenshot folders — new adjacent competitor for the History track [S6+9d adjacent]. CVE-2026-33829 (Snipping Tool NTLM hash leak via `ms-screensketch` URI scheme) is the cautionary design tale for our planned `snapture://` URL handler [S6+9d sec]. This refresh also closes adversarial-audit gaps: ICC profile awareness, multi-GPU `WM_DISPLAYCHANGE` resilience, RDP / VM-guest capture matrix, secure-desktop / UAC dim / DPI-change behavior during record, log-redaction policy, Native AOT investigation, history-DB backup, HIPAA/PHI rule pack, settings/index migrations, performance/power budget, and a Compliance cross-cutting track for GDPR lawful basis.

---

## North star

A Windows screenshot tool the audience that bounced off Snagit's 2025 subscription pivot [S2], CleanShot X being Mac-only [S6], Lightshot's Aug-2025 enterprise-block + 22-month-broken-on-Win11 abandonment [S6], and the Snipping Tool 25H2 / Jan-2026 KB5074109 reliability crashes [S6] would actually pick. Six anchors:

1. **WinRT capture parity** with `Windows.Graphics.Capture` (HDR, per-monitor DPI, no flicker, no black Chromium frames, `DirtyRegionMode` + FP16 + `IncludeSecondaryWindows` on 24H2+) [S3, S6].
2. **Annotation editor** that preserves every shape forever, exports SVG, and ships hand-drawn aesthetics [S5/S4].
3. **Smart Capture** (UIA-driven element selection during snip) — a capability no consumer screenshot tool currently ships during capture [S4]. Ships everywhere — not Copilot+ gated like Snipping Tool's "Object Selector" [S6].
4. **Auto-redact** secrets locally with model + regex pack — the Lightshot anti-pattern made into a feature. Snipping Tool's "Quick Redact" catches phones+emails (2 things). Snapture catches 30+ rule classes locally [S5/S2/S6].
5. **No telemetry, ever.** Update checks and crash dumps are explicit user actions. State this on the README first line and in the privacy doc [S5/S2].
6. **Modern AI surface — strictly local.** Foundry Local / Ollama / Phi-3.5-vision ONNX integrate as opt-in providers (resolves v0.4 ONNX ambiguity; matches PowerToys 0.96 Advanced-Paste's local-only AI providers) [S6]. **Cloud LLM endpoints stay rejected** — Lightshot is the cautionary tale, not the product brief.

Non-goals (rejected as misfits — see §Rejected): hosted cloud sharing, account systems, mobile app, AI summarization that round-trips a cloud server, paid tiers, telemetry of any kind.

---

## v0.1.0 — Shipped 2026-05-08
`[x]` Region/Window/Fullscreen capture (GDI), global hotkeys, tray, basic editor (view/save/copy/pin), pin window, JSON settings, Catppuccin Mocha theme, GitHub Actions release workflow [self].

## v0.2.0 — Shipped 2026-05-08
WinRT engine + GDI fallback, picker bypass, `WDA_EXCLUDEFROMCAPTURE`-aware, AppUserModelID, borderless consent, PrintScreen-24H2 hijack detect, last-region recapture (`Shift+PrintScreen`), self-timer 1/3/5/10s, window-pick overlay with PgUp/PgDn ancestor walk. Skia annotation editor with vector model + 12 tools + brightness/contrast/grayscale/invert + frame wrappers + `.snapture` round-trip + WebP export. Tabbed Settings with live hotkey recorder + import/export + engine capability detect. Color picker (HEX/RGB/HSL/APCA-Lc), pixel ruler, magnifier loupe, pin polish (opacity/border/shadow/Alt-click/solo/hide-all) [self / S3 / S5].

## v0.3.0 — Shipped 2026-05-08
Built-in OCR via `Windows.Media.Ocr`, OCR region tray flow, History row "Run OCR", "OCR all" bulk index. SQLite + FTS5 capture history at `%LOCALAPPDATA%\Snapture\history\index.db` with auto-tag (process + window title) + thumbnail wall + debounced search + right-click menu (open / pin / re-OCR / reveal / delete). Scrolling capture alpha via UIA `IScrollProvider` [S3 / S5].

## v0.4.0 — Shipped 2026-05-08
**Differentiator wave:**

- Auto-redact secrets — `SecretDetector` (Gitleaks-derived rule pack: AWS / GCP / Azure / GitHub / Stripe / Slack / Twilio / JWT / npm / generic hex + PII Luhn-validated cards / SSN / IBAN / IPv4 / MAC / email). `AutoRedactor` re-runs OCR + drops `RedactShape` solid-fills per word-box. Each redaction is its own undo-stack entry [S5 §4].
- LAN-only share server — Kestrel minimal API, single-adapter binding (never `0.0.0.0`), 24-byte URL-safe-base64 single-fetch tokens, TTL-bounded, opt-in Settings tab [S5 §12].
- UIA Smart Element Capture — non-activating overlay highlights individual UIA controls in real time, PgUp climbs parent chain, click captures element rect [S2 §1, S4 a11y-insights].
- Plugin SDK — `Snapture.Plugin.Abstractions` multi-target library (`netstandard2.0` + `net10.0`); `IDestination` / `ICaptureProcessor` / `IEditorEffect` / `IPluginHost` contracts; collectible `AssemblyLoadContext`s; `[SnapturePlugin]` attribute with capability flags; Plugins window in tray [S5 §9].
- Step Capture mode — `WH_MOUSE_LL` hook records every click, snapshots foreground window, exports Markdown bundle `steps.md` + `images/` [S2 §1, S4 Scribe].

## v0.5.0 — Shipped 2026-05-08
Image-stitch fallback for scrolling capture — pure-managed subsampled-SAD seam alignment (80-row strip search, ≥0.92 confidence, naive-concat fallback). Browser pages now stitch cleanly [S5 §2]. Carbon-style code-window chrome export wrapper (36px macOS title bar with traffic-light dots, dark-gray bar, 14px rounded outer corners) [S4].

## v0.6.0 — Shipped 2026-05-08
- Sticky-header / sticky-footer detection — `ImageStitcher.DetectStickyStrips` finds rows pixelwise-stable across every frame; emitted once at top + bottom of the stitched output, body stitched through middle frames [S4].
- Animated GIF recording — `Services/GifRecorder` + `Views/GifRecordingWindow` (foreground window or virtual screen, 10 fps default, AnimatedGif library) [S2 §7].
- Per-rule auto-redact toggles — Settings → Auto-redact tab + persisted `DisabledRedactRules` (new rules ship enabled).
- Plugin contract widening — `ICaptureProcessor.ProcessAsync` may now resize; processor runs **before** save so output lands in the file + history [S5 §9].
- `docs/HOTKEYS.md` + `docs/CAPTURE-MATRIX.md` + `manifests/SysAdminDoc/Snapture/0.6.0/` (winget multi-file 1.7.0).

## v0.6.0+theme — Shipped 2026-05-08 (unreleased point release)
All thirteen view XAMLs and code-behinds switched to `App*` semantic tokens (`AppBackground`, `AppSurface`, `AppCanvas`, `AppBorder`, `AppBorderStrong`, `AppForeground`, `AppMutedForeground`, `AppSubtleForeground`, `AppAccent`, `AppWarning`). Both Catppuccin palettes (Mocha + Latte) keep exporting legacy names for plugin-API compatibility but Snapture's own chrome no longer leaks specific palette flavor names. Tray About reads version from assembly + surfaces active theme + effective mode for diagnostics. `ThemeManager.NormalizeMode` fixes tray theme menu's `IsChecked` comparison.

---

## v0.7 — Recording wave (NOW)

The release that turns Snapture from "snip + edit" into "snip + edit + record." Validated by the most-upvoted single feature ask across the entire OSS field (ShareX #6688 HDR — 108 reactions; Flameshot #172 GIF — 126 reactions; Cap's entire 18,733-star existence) [S6].

**Competitive narrowing as of 2026-05-17:** Snipping Tool's Insider channel began rolling out "Perfect Screenshot" (AI auto-crop) + native HEX/RGB/HSL color picker — bumps the smart-crop and color-picker UX from "differentiator" to "parity-or-better" for Snapture. Snapture's APCA-Lc readout + Copilot+-independent smart-element capture (UIA, ships everywhere, not Copilot+ gated) remain ahead. "Ask Copilot" was also removed from Snipping Tool in the same wave — the OS-bundled tool just narrowed its AI surface, validating Snapture's local-only AI carve-out [S6+9d community].

### v0.7.1 — Video recording (MP4 / HEVC / AV1)

### v0.7.2 — GIF + modern formats


### v0.7.3 — HDR + modern still formats


### v0.7.4 — Capture quality


### v0.7.5 — Distribution + Recording polish


---

## v0.8 — Editor + AI-local + History wave (NEXT)

The release that closes the editor-polish gap with Snagit / Cap / openscreen and adds the local-AI carve-out.

### v0.8.1 — Editor open-existing + autosave


### v0.8.2 — Background Beautifier + Spotlight + modern shapes


### v0.8.3 — OCR + extraction + step-capture exports


### v0.8.4 — History + library polish


### v0.8.5 — Filename + window-context tokens

### v0.8.6 — Local-AI opt-in (resolves the v0.4 ONNX ambiguity)


### v0.8.7 — Clipboard + integration


---

## v0.9 — Distribution + Plugins + i18n (LATER)

The release that puts Snapture in front of the audience that already uses winget / Chocolatey / Scoop / MS Store, in their language.

### v0.9.1 — Code-signing + signed installers


### v0.9.2 — Distribution channels


### v0.9.3 — Plugin SDK polish


### v0.9.4 — i18n / l10n


### v0.9.5 — Import polish


---

## v1.0 — Studio + WinUI 3 differentiator extensions (LATER)

Features that earn their place once the core is solid.


---

## Stretch / nice-to-haves


---

## Rejected (with reasoning)

These items came up in research and are deliberately **not** roadmap candidates. Listed so they don't get silently resurrected.

- **Hosted cloud sharing (Imgur, Gyazo, Streamable, CleanShot Cloud, Loom-style instant link, Lightshot prnt.sc)** — contradicts the "no cloud" anchor. The Lightshot scandal escalated in 2025: Missouri S&T enterprise-blocked it in Aug-2025 [S6 community]; v7.0.1 (Jul-2024) broke screenshot capture entirely on Win11 + macOS — dev hasn't shipped a fix in **22 months as of May 2026** [S6 community]. **User-controlled LAN / self-hosted endpoints (XBackBone / Slink / ShotShare / Myazo / NextCloud / Immich) are NOT cloud and stay on the roadmap** [S4, S6 OSS].
- **Account systems / SSO / team workspaces** — not the audience; collaboration belongs in the file-sync layer the user already chose [S2 FuseBase rebrand]. CleanShot Cloud's SSO + SCIM moat (2025-2026) is exactly what we're not building.
- **Anonymous metrics / telemetry / Sentry / AppCenter** — explicit non-goal. A feature, not an oversight. State in README + privacy doc [S5 §15]. OBS 32.0's opt-in-only crash-log upload (privacy-respecting) is the reference pattern if we ever add it [S6 OSS].
- **Cloud LLM endpoints (OpenAI, Gemini, Anthropic, OpenRouter)** — Snipping Tool's path; ShareX 19's "Analyze image" model picker; Snapture stays out. **Local LLM endpoints (Ollama / Foundry Local / Phi-3.5-vision ONNX) are explicitly allowed** as an opt-in v0.8.6 carve-out — clarifies the v0.4 ONNX ambiguity. Resolution rule: if the byte path is `Snapture.exe → localhost`, it's allowed; if it's `Snapture.exe → external host`, it's not [S6 OSS, S6 adjacent].
- **AI-summarize / cloud transcription / "Ask Copilot about this screenshot"** — Snipping Tool 26H1 path; fine for them, not us. Same rule as cloud LLM: local-only opt-in is allowed [S5 §4, S6 platform]
- **Imgur / Dropbox / Box / Flickr / Jira / Confluence destination plugins shipped in-box** — Greenshot's bloat lesson. Plugin SDK supports them; we don't bundle them [S1]. SnapX explicitly removed 15 dead uploaders for the same reason [S6 OSS].
- **Browser-extension-only product (Nimbus / FuseBase pattern)** — not a Windows-tool, not the brief [S2 §9]
- **Subscription pricing / paid Pro tier** — explicit non-goal; the entire pitch is "no subscription." Snagit 2025-02-12 pivot is the cautionary tale; their community fled [S2 §1, §6]
- **MS Store as exclusive channel** — restricts MSI / portable distribution, blocks low-level hooks if we ever add them. Ship in Store, but never *only* Store [S3 §9]
- **OBS-style signed-injection game capture** — anti-cheat triggers, per-game-cert friction. DXGI Desktop Duplication in a separate process is the supported path under Stretch [S6 platform]
- **Cloud-rendered "make real" sketch-to-HTML / screenshot-to-code** — Stretch carves out a local-only opt-in equivalent; cloud is rejected for the same reason as cloud LLM [S6 OSS, S6 adjacent]

---

## Cross-cutting tracks (run alongside every release)

### Security & privacy


### Accessibility


### Internationalization (i18n / l10n)

Phase-1 locales (v0.9.4): en-US (canonical), de, fr, es, it, pt-BR, nl, pl, cs, ru, tr, ja, zh-Hans, zh-Hant, ko, **ar (RTL — every competitor has it)** [S1, S6 OSS]. OCR coverage matches.

### Observability (local-only)


### Testing


### Documentation


### Distribution & ops


### Migration plan (existing GDI users → WinRT)

1. **v0.2.0 release shipped both engines.** `auto` default picks WinRT on Win10 1809+, GDI on older. Existing settings preserved. ✅
2. **First-run consent prompt** for `GraphicsCaptureAccess.RequestAccessAsync(Borderless)` ✅ [S3 §1.4]
3. **One-shot toast on first WinRT capture** ✅
4. **GDI engine remains shipped through v1.0** as fallback; deprecation only after WinRT proves zero-regression for 6 months.
5. **Settings file forward-compat** — add fields with safe defaults; never break old `settings.json`. ✅
6. **`.snapture` v1 format** locked at v0.2.2 release; v2 (when it comes) writes a `version` field and v0.2 reader rejects unknown versions cleanly.

### Migration plan (data + state, audit-gap fill)


### Performance & power budget (new track, audit-gap fill)

A Snapture install runs WPF + Skia + SQLite + Kestrel + plugin host + tray + hotkey window. That's a non-trivial baseline. Competitors like wcap (1.2k★) emphasize a 10MB-class footprint and Snapture's feature delta is much wider; the trade-off must still be quantified.


### Compliance (new track, audit-gap fill)


---

## Competitive watch list

OSS reference baseline:

- **Greenshot** (4,913★, last commit 2026-04-22, no stable since 1.3.x — slow maintenance) — destination-plugin bloat is the lesson; HiDPI editor icons (#115), HDR (#542), MSI installer (#842), tabbed editor (#1079), pick-mode-after-hotkey (#1035) are the active asks [S1, S6 OSS]
- **ShareX** (37,386★, v20.1.0 2026-05-06; v20 ships **Avalonia image editor with 18 tools + 232 effects**, **native ARM64 via MS Store**, **AI Analyze image** w/ OpenAI/Gemini/OpenRouter, **Background Beautifier**, **Spotlight tool**, modern arrows). Destination workflow concept fine if user-configured per endpoint; never bundle 80+ [S1, S6 OSS]
- **Flameshot** (29,853★, v14.0.rc1 2026-04-06 + v13.3.0 stable). v13 added **secure pixelation that only uses outside pixels**, dark mode on Windows, **Imgur disabled by default at compile** for privacy. v14 added **Snipping-Tool registry-takeover detection**, **Capture Active Monitor**, **Portable Binary Mode** with `flameshot.ini`. Top issue #240 (171 reactions) "Load existing image and edit" is **the most-upvoted feature ask in the entire OSS field** [S1, S6 OSS]
- **CleanShot X** — Mac-only as of 2026-05-08; no Windows port rumored. Their entire moat now is Cloud (transcripts, sharing, branding) which is what we're not building. Direct gap [S2 §2, S6 commercial]
- **Snagit 2026** ("Camtasia Snagit" rebrand) — Step → PowerPoint / Word, Smart Move + Smart Redact AI, OCR engine improvements, editable cursor in step guides. Subscription-only since 2025; community fled [S2 §1, S6 commercial]
- **Shottr** — Mac-only $12 lifetime; OCR remove-linebreaks, OKLCH color, APCA contrast (Snapture has APCA) [S2 §3, S6 commercial]
- **Snipaste** — Windows-only; Pro features (custom-snip-dialog, CLI `--hold` / `--block`, hot-corner toggle, refresh-preserving-annotations) are paywalled, all are roadmap candidates [S2 §6, S6 commercial]
- **ScreenToGif** (26,903★, v2.43.1 2026-03-22) — frame-by-frame editor; cache bloat is a cautionary tale; **plugin page now auto-downloads ffmpeg** is the dependency-pull pattern; HDR breaks usability (#1452); ring-buffer (#1009) and animated AVIF (#1171) are open [S2 §7, S6 OSS]
- **Snipping Tool 11.2508+** — Capture Bar OCR, QR scanner, color picker, **HDR Color Corrector** (back), Quick Markup, Visual Search via Bing, Quick Redact (catches phones+emails only), AI Smart Crop / Object Selector / Click to Do **all Copilot+ PC only and Snapdragon X2 only on 26H1**. Win11 25H2 actively breaks Win+Shift+S after several uses [S6 community + commercial]
- **PowerToys 0.99 (2025)** — ZoomIt now in PowerToys (overlaps Snapture's loupe / color picker / ruler / scrolling); **Advanced Paste plumbed Foundry Local + Ollama** as on-device AI providers (precedent for v0.8.6 carve-out); PowerRename added EXIF tokens (precedent for v0.8.5) [S6 adjacent]
- **Cap** (CapSoftware/Cap, 18,733★, ~84 releases in 2026, Tauri/Rust/TS) — **MediaFoundation + zero-copy Dx12 backend on Windows** (v0.4.7); **captions + keyboard-press as editable timeline tracks** (v0.4.81); cloud AI features paywalled [S6 OSS]
- **openscreen** (siddharthvaddem/openscreen, 35,302★, v1.4.0 2026-05-06, Electron) — **cursor-telemetry-driven auto-zoom suggestions**; **persistent edit projects**; **webcam shape masks**; **dual-frame preset**; **mouse highlighter + click emphasizer** [S6 OSS]
- **snow-shot** (mg-chao/snow-shot, 4,521★, Tauri/TS) — plugin architecture with video / OCR / translation / AI dialog as plugins (validates Plugin SDK approach) [S6 OSS]
- **eSearch** (xushengfeng/eSearch, 6,386★, Electron) — **Screen Translation overlay**, **reverse image search**, omnidirectional scrolling stitching, APNG export, multimodal LLM image-discussion [S6 OSS]
- **SnapX** (SnapXL/SnapX, 919★, ShareX fork on .NET 10 + Avalonia) — direct .NET 10 competitor (track migration progress); **post-quantum-resistant secret encryption** for upload credentials; **RapidOCR over Tesseract** validation [S6 OSS]
- **WinShot** (mrgoonie/winshot, 535★, Wails Go+React) — fast cadence, R2 upload first-class. Validates appetite for fresh Windows-screenshot tools [S6 OSS]
- **NormCap** (dynobo/normcap, 2,602★) — OCR-first capture; "capture information instead of images" — validates Capture Text hotkey pattern [S6 OSS]
- **Peekaboo** (openclaw/Peekaboo, 3,270★) — macOS CLI + **MCP server for AI agents** to request screenshots. **No Windows OSS equivalent** — Snapture's MCP-server path (now in v0.9.2, promoted from Stretch this refresh) is greenfield [S6 OSS, S6+9d OSS]
- **wcap** (mmozeiko/wcap, 1,194★) — minimalist C; **fragmented MP4 default**, **app-local audio capture**, AAC or FLAC, AV1 + HEVC 10-bit. Fragmented-MP4 pattern adopted in v0.7.1 [S6 OSS]
- **OBS Studio** (72,237★, 32.1.2 2026-04-21) — graphics-hook is the inspiration for game capture; **plugin manager UI** (32.0) is the in-box-installer pattern; **opt-in crash-log upload** is the privacy-respecting telemetry pattern [S6 OSS]
- **screenshot-to-code** (abi/, 72,476★) — drop screenshot → output HTML/Tailwind/React/Vue. Local-LLaVA-via-Ollama pairing under Stretch [S6 OSS]

**Watch list — 2026-05-09 → 17 additions:**

- **Peekaboo v3.0 → v3.2.0** (openclaw/Peekaboo, 3,270★, May 9-15 2026) — macOS-only screenshot + MCP-server-for-AI-agents. v3.0 unified screenshot + UI detection, v3.1.0 introduced daemon-backed lightweight-metadata returns, v3.2.0 (May 15) added pluggable local-model provider syntax `ollama/<model>` and `lmstudio/<model>` — directly applicable to Snapture v0.8.6 [S6+9d OSS]
- **Screenity v4.4.0 → v4.4.7** (alyssaxuu/screenity, 18.2k★, May 9-17 2026) — Chrome extension; pivoted to **WebCodecs + OPFS direct-to-disk** writes for long recordings, with `MediaRecorder` fallback. Architecture reference for v0.7.1 streaming-to-disk + crash-recovery [S6+9d OSS]
- **Greenshot continuous builds 1.4.187 → 1.4.191** (May 13-14 2026) — capture-correctness enhancement + startup-crash fix + DE/IT/FR translation updates. Still no stable 1.4 [S6+9d OSS]
- **Cap v0.4.86 / v0.4.87** (CapSoftware/Cap, May 16 2026) — release-pipeline hotfix only; active-maintainership signal [S6+9d OSS]
- **Raycast 2.0 Windows Public Beta** (May 14 2026) — first cross-OS Raycast version. v0.58 / v0.59 added "scan sub-directories for screenshots" (screenshot library indexing) and "Save as File" for clipboard images. Adjacent commercial competitor for Snapture's History track — different category (launcher) but same audience [S6+9d adjacent]
- **snow-shot (mg-chao/snow-shot, 4,521★)** — **pre-abandonment signal**: 18-month tag silence despite active issue tracker. Watch for unmaintainer announcement; if it abandons, Snapture's plugin-architecture validation loses one data point and the snow-shot user base is in play [S6+9d OSS]
- **Snipping Tool "Perfect Screenshot" + native Color Picker** rolling out via Insider 11.2504.38.0+ (May 8-17 2026) — AI auto-crop (Copilot+ only) + HEX/RGB/HSL picker. Incumbent moves onto Snapture's v0.7 smart-crop + v0.2.4 color-picker turf. Snapture still differentiates on APCA-Lc + Copilot+-independent UIA Smart Element Capture [S6+9d community]
- **NVIDIA ShadowPlay / AMD ReLive / Xbox Game Bar Game DVR** — consumer game-clip recorders. Snapture's v0.7.1 ring-buffer competes directly with ShadowPlay Instant Replay. Per-vendor driver dep is the moat; Snapture's WGC-based path runs anywhere [S6+9d adjacent]
- **Carnac / KeyCastr** — keystroke-overlay incumbents on Windows / macOS. Snapture's v0.7.1 keystroke overlay track is the integrated alternative; bundled with capture means no second tool to install [S6+9d adjacent]
- **Pikka / Monosnap / Gyazo Pro** — Mac-first + cross-platform tools repeatedly surfaced in "CleanShot alternative" lists. Not Windows-priority but tracked for feature ideas (Gyazo's auto-OCR-on-upload pattern interesting if we ever ship the LAN-share OCR-index trick) [S6+9d adjacent]
- **Skitch successor 2026** — Evernote-era annotator still has an active "what replaced Skitch" cohort. Snapture's editor, drag-image-to-tray, and pinned-overlay set match the lost-Skitch workflow more closely than any incumbent [S2 ref §3, S6+9d community]

---

## Appendix — Source bundles

Each `[Sn]` reference in the body maps to one of these dossier bundles. Every concrete claim, feature attribution, complaint, or library recommendation traces here.

### S1 — OSS competitors (foundation: Greenshot, ShareX, Flameshot)
- Greenshot — github.com/greenshot/greenshot (4,913 stars 2026-05-08, no stable since 1.3.x; last commit 2026-04-22, GPL-3.0)
- ShareX — github.com/ShareX/ShareX (37,386 stars, v20.1.0 2026-05-06, GPL-3.0) — getsharex.com/changelog
- Flameshot — github.com/flameshot-org/flameshot (29,853 stars, v14.0.rc1 2026-04-06 + v13.3.0 stable 2025-10-28, GPL-3.0)
- Greenshot top issues: #115 HiDPI editor (18), #542 HDR (12), #635 Choco outdated (10), #525 .NET 8/9/10 (7), #842 MSI (3), #103 lettered counter (7), #311 curved arrows (6), #624 scrolling (5), #696 auto-border (3), #1063 postpone-quick-toggle, #1035 capture-mode picker, #1079 tabbed editor (3), #1106 crosshair customization, #1172 visual capture-feedback, #1181 lean-build, #1063 quick-postpone, #97 Ctrl+wheel resize, #348 Win11 clipboard, #562 frozen-stable, #579 CVE-2023-34634, #375 .greenshot format
- ShareX top issues: #6688 HDR (108, 222 comments), #6090 WebP/AVIF/JXL (37), #5250 WebP (30), #6205 rotate shapes (24), #5312 HiDPI multi-monitor (18), #6653 ARM64 (15), #848 game capture (13), #4616 vertical text (13), #3779 batch (11), #3992 multi-uploader (10), #4227 more arrow shapes (8), #3474 auto-canvas (7), #4381 aspect-ratio (7), #7278 balloon corner radius (6), #3605 Windows Share (6), #2956 color palette (5), #8373 Immich (5, Jan 2026), #293 DX11 fullscreen (9 yrs open)
- Flameshot top issues: #240 load-existing-image (171), #1130 scrolling (127), #172 GIF (126), #5 visual window picker (121), #604 cursor-in-screenshot (109), #702 OCR (58), #511 QR (22), #313 drop shadows (23), #249 picker opacity (16), #690 auto-border (16), #2055 autosave drafts (18), #954 pin tool (21), #50/#499/#27 custom uploaders, #3465 PrintScreen 24H2 disc, #3783 PrintScreen 24H2 issue
- ShareX changelog v15-v20 + getsharex.com/changelog + getsharex.com/docs/scrolling-screenshot + github.com/ShareX/CustomUploaders
- Flameshot v13.0 Aug-2025 release notes; v13.3.0 Oct-2025; v14.0 RC1 Apr-2026 release notes
- flameshot.org/docs/guide/key-bindings/

### S2 — Commercial competitors (Snagit, CleanShot X, Shottr, PicPick, FastStone, Snipaste, ScreenToGif, Snipping Tool, Nimbus/FuseBase, Lightshot)
- Snagit ("Camtasia Snagit" rebrand) — techsmith.com/snagit (subscription-only since 2025-02-12, ~$39/yr); 2026 features: Step Capture → PowerPoint + Word, OCR engine improvements, editable cursor, Smart Move, Smart Redact, Image Simplifier, Background Remover, Panoramic/Scrolling, Library, Slack/Teams share, MSI+MST enterprise deploy
- CleanShot X — cleanshot.com (Mac-only $29 hybrid + $8/mo cloud); 2025-2026 changelog: Tahoe interface refresh, All-In-One UI freeze, Setapp unlinking, video transcription pulled in front of paywall, SSO+SCIM 2FA branded sharing — entire moat is Cloud; **no Windows version exists or is rumored** as of 2026-05-08
- Shottr — shottr.cc (Mac-only $12 lifetime); 2025-2026: OCR remove-linebreaks, OKLCH, APCA, expandable background, raster, scrolling-cursor-in-middle fix
- Snipaste — snipaste.io ($19.99 Pro); Pro: custom-snip-dialog, CLI `--hold`/`--block`, hot-corner trigger, solo mode, refresh-preserving-annotations, hierarchy, OCR API. Windows-only; Linux/macOS 2.x stable not shipped as of 2026-05
- PicPick — picpick.app ($24 commercial); 7.4.1 (Nov 2025) bug fix; betas exploring layer support + WebP
- FastStone Capture — faststone.org ($19.95 lifetime); 11.2 Nov-2025 minor; 11.0 Jan-2025 transparent-Edge tool; WMV-only video remains the gap
- ScreenToGif — github.com/NickeManarin/ScreenToGif (MS-PL, 26,903 stars, 2.43.1 2026-03-22); cache bloat #720; #1009 ring buffer; #1171 animated AVIF; #1450 JXL; #1452 HDR breaks; #1385 Win11 jump list; #1439 NextCloud; auto-FFmpeg-download in 2.43
- Snipping Tool (Win11) — Text Actions/OCR (23H2), QR (24H2), Image Eraser (24H2), Quick Redact (phones+emails only), scrolling capture (25H2), HDR Color Corrector toggle, Capture Bar OCR (11.2508), Visual Search via Bing, Quick Markup (11.2508.24), Color Picker, Perfect Screenshot (Copilot+ only), Object Selector (Copilot+ only), Click to Do (Copilot+ only), unbranding from Copilot
- Tella, Loom, Claap, Scribe — adjacent: Tella auto-zoom + Find Mistakes + Dec-2025 highlight/blur; Loom 2025 outages; Claap Lemlist acquisition Nov-2025; Scribe blank-scribes Win bug
- Nimbus/FuseBase — thefusebase.com (rebrand 2024 lost users)
- Lightshot — app.prntscr.com — sequential URL enumeration scandal; **Missouri S&T enterprise-blocked Aug-2025**; v7.0.1 (Jul-2024) broke Win11+macOS — dev hasn't shipped a fix in 22 months
- Vendor pages, support docs, changelogs, Neowin/WindowsCentral/LaptopMag/Thurrott/XDA reviews, Wikipedia entries, AlternativeTo/G2/Capterra reviews

### S3 — Windows capture API dossier (foundation)
- Windows.Graphics.Capture — learn.microsoft.com/uwp/api/windows.graphics.capture; Win10 1803+, window-mode 1809, IsCursorCaptureEnabled 2004, IsBorderRequired/IncludeSecondaryWindows/MinUpdateInterval 22H2, **DirtyRegionMode 24H2, FP16 framepool guidance for HDR, IsHdrSupported / DXGI_OUTPUT_DESC1.ColorSpace = DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 detection**
- DXGI Desktop Duplication — learn.microsoft.com/windows/win32/direct3ddxgi/desktop-dup-api; one instance per process limit
- Magnification API — learn.microsoft.com/windows/win32/api/magnification/; STA helper-process pattern
- Windows.Media.Ocr — learn.microsoft.com/uwp/api/windows.media.ocr; ms-settings:regionlanguage-adddisplaylanguage
- UI Automation — IUIAutomationScrollPattern, FlaUI, Chromium 130+ default UIA backend
- HDR — DXGI scRGB FP16; ACES tone map; Win2D HdrToneMapEffect; JPEG XR archival caveats
- Media Foundation SinkWriter, **D3D12 Video Encode 1.1 (AV1 in Win11 24H2 / WDDM 3.2)**, MFTEnumEx fallback chain
- Win11 Snipping Tool feature additions through 25H2 + 26H1 Insider (CreateForDisplayId, Phone Link capture as window not source frames)
- winget manifest spec **1.7.0 (May 2025) → 1.9.0 → 1.10.0** referenced in 2025 build automation; MSIX vs portable tradeoffs
- microsoft/Windows.UI.Composition-Win32-Samples, microsoft/PowerToys (MeasureTool/ColorPicker), obsproject/obs-studio (graphics-hook), sskodje/ScreenRecorderLib, cube0x8/Capso, microsoft/Windows-classic-samples (DXGIDesktopDuplication, Magnification), robmikh/Win32CaptureSample (canonical WGC sample, FP16/HDR paths)

### S4 — Community pain points & adjacent tools (foundation)
- Reddit r/windows, r/Windows11, r/sysadmin, r/screenshots — scrolling missing, HDR washed out, multi-monitor DPI bugs, fullscreen-game black, OCR opaque
- Microsoft Q&A — snipping tool aspect-ratio, multi-monitor 125%, HDR over-exposure, secondary monitor invisible, "doesn't scroll", "what happened to snipping tool"
- Tech Community: "New Snipping Tool is complete and absolute garbage" (4440555)
- HN: 46815297 HDR, 40650844 Flameshot+Tesseract, 26113753, 26446070, 30071766, 26168285
- Lightshot: Kaspersky cryptoscam, Missouri S&T ban (econnection.mst.edu 2025-08-05), Wired report
- ZxIght, getsharex/docs/scrolling, capture-full-page.com
- Adjacent: Excalidraw (~85k★), tldraw (~45k★), Carbon, Ray.so, Snappify, Tella, Loom, Raycast, Multi.app, OBS, Eagle, ImageGlass, Joplin/Obsidian web clippers, PowerToys Screen Ruler / Image Resizer / Always-On-Top, Sysinternals/NirSoft portable doctrine, Inspect.exe / Accessibility Insights, Cap (Rust/Tauri), xland/ScreenCapture, WinShot, Capter, d3dshot (DX11/12), GoFullPage / FireShot, XBackBone / Slink / ShotShare / Myazo
- Awesome lists: reg-viz/awesome-screenshot, aitaskorchestra/awesome-screenshot-tools, 0PandaDEV/awesome-windows, SonicZhu/Awesome-Windows, deadcoder0904/awesome-website-screenshots, awesome-selfhosted, GitHub topic:screen-capture / topic:screen-annotation
- Roundups: pimpmysnap, ghacks, howtogeek, screensnap.pro, techbloat, zight

### S5 — Technical implementation building blocks (foundation)
- §1 Vector annotation: SkiaSharp.Views.WPF (MIT, primary), Win2D, custom DrawingVisual; RBush.NET hit-test; SVG via SKSvgCanvas / Svg.NET; tldraw record-store pattern; .snapture zip = document.json + background.png + assets/
- §2 Stitching: Math.NET Numerics FFT (pure-managed, MIT), OpenCvSharp4 (Apache-2, ORB/AKAZE), FlaUI (UIA driver, MIT), avoid Emgu.CV (commercial)
- §3 OCR: Windows.Media.Ocr default; RapidOCR.Net (Apache-2) bundled fallback; PaddleOCR overkill
- §4 Secret detection: Gitleaks rules (MIT) + detect-secrets ports; Presidio recognizer ports; RapidOCR bundled DBNet for text-region boxes (one ONNX serves OCR + redact)
- §5 Inpaint: LaMa ONNX (CC-BY-NC-SA caveat); AOT-GAN (Apache-2 code+weights, license-clean alternative)
- §6 GIF/MP4: FFMpegCore (MIT) + LGPL FFmpeg builds (BtbN); Magick.NET ≥ 14.12.0 floor; Vortice.Windows for SinkWriter; ScreenToGif Editor.xaml as MS-PL reference
- §7 Theming: CommunityToolkit.Mvvm; WPF-UI (lepoco, MIT); Catppuccin Mocha + Latte hand-rolled ResourceDictionary
- §8 Hotkey: NHotkey (Apache-2 wrapper); avoid SetWindowsHookEx; PrintScreen Win11 24H2 registry toggle
- §9 Plugins: AssemblyLoadContext (collectible); IDestination/IEditorTool/IEffect/ICaptureSource; capability manifest; Greenshot shape design only; OBS 32.0 plugin manager UI + min/max-host-version
- §10 Auto-update: Velopack (MIT); SignPath OSS (free EV); Azure Artifact Signing fallback; Certum / Sectigo OV alternatives
- §11 Distribution: winget multi-file YAML 1.7 → 1.9 → 1.10; Chocolatey + Scoop; MSIX vs portable
- §12 LAN server: Kestrel minimal API; Makaretu.Dns mDNS; per-adapter binding; INetFwPolicy2 firewall (private profile only); RandomNumberGenerator one-time tokens
- §13 DB: Microsoft.Data.Sqlite + FTS5; LiteDB for portable; sqlite-net-pcl alternative
- §14 Color picker: PowerToys ColorPicker XAML reference
- §15 Observability: Serilog file + async sinks; PowerToys Bug Report Tool diagnostic-dump pattern; explicit-update-check via api.github.com/repos/SysAdminDoc/Snapture/releases/latest

### S6 — 2025-2026 research delta (NEW)

#### S6 OSS — recent OSS releases & top-issue mining
- **Greenshot continuous builds** (no stable 1.4 yet) — github.com/greenshot/greenshot/releases — Apr-Apr 2026 14 builds
- **ShareX v17 (2025-01) → v18 (2025-08, .NET 9 + SQLite history) → v19 (2026-01 Analyze image AI + Spotlight) → v20.0.2 (2026-04-24 Avalonia editor, ARM64) → v20.1.0 (2026-05-06)** — getsharex.com/changelog
- **Flameshot v13.0 (2025-08) Qt6 + Win dark mode + WebP + secure pixelation + reverse-arrow + KDE-Connect; v14.0 RC1 (2026-04) Snipping-Tool registry-takeover detection + Capture Active Monitor + Portable Binary Mode** — github.com/flameshot-org/flameshot/releases
- **ScreenToGif 2.43 (2026-03-20) — auto-FFmpeg-download in plugin page** — github.com/NickeManarin/ScreenToGif/releases/tag/2.43
- **Cap (CapSoftware/Cap, 18.7k★, 84 releases in 2026)** — v0.4.7 (MediaFoundation + Dx12 zero-copy on Windows), v0.4.81 (timeline keyboard tracks), v0.4.84 (Ultra preset, segmented HLS uploads, click-to-lock window selection) — github.com/CapSoftware/Cap/releases
- **openscreen (siddharthvaddem/openscreen, 35.3k★)** — v1.0.0 (2025-12-03) → v1.4.0 (2026-05-06): magic-wand auto-zoom, persistent edit projects, webcam shape masks, dual-frame preset, mouse highlighter, blur-parts-of-video — github.com/siddharthvaddem/openscreen/releases
- **snow-shot (mg-chao/snow-shot, 4.5k★)** — plugin architecture validates Plugin SDK demand — github.com/mg-chao/snow-shot
- **eSearch (xushengfeng/eSearch, 6.4k★, v15.2.3 2025-12-27)** — Screen Translation, reverse image search, omnidirectional scrolling, APNG export, multimodal LLM image-discussion — github.com/xushengfeng/eSearch/releases/tag/15.2.3
- **SnapX (SnapXL/SnapX, 919★, v0.4.0 2026-02-20)** — ShareX fork on .NET 10 + Avalonia, RapidOCR, AVIF/WebP, encrypted secrets-at-rest — github.com/SnapXL/SnapX/releases/tag/v0.4.0
- **WinShot (mrgoonie/winshot, 535★)** — Wails Go+React, R2 upload — github.com/mrgoonie/winshot/releases
- **Capter (decipher3114/Capter, 226★, v4.0.1 2025-07)** — Rust cross-platform, organize mode — github.com/decipher3114/Capter
- **NormCap (dynobo/normcap, 2.6k★, v0.6.0 2025-08)** — OCR-first capture — github.com/dynobo/normcap
- **wcap (mmozeiko/wcap, 1.2k★)** — fragmented MP4 default, app-local audio — github.com/mmozeiko/wcap
- **xcap (nashaofu/xcap, 963★)** — Rust capture lib used by Cap and Capter — github.com/nashaofu/xcap
- **Peekaboo (openclaw/Peekaboo, 3.3k★)** — macOS CLI + MCP server for AI agents — github.com/openclaw/Peekaboo
- **OBS Studio 32.0 (2025-09-22) + 32.1.x (2026)** — plugin manager, opt-in crash-log upload, plugin forward-compat guard, hybrid MP4 default — github.com/obsproject/obs-studio/releases
- **screenshot-to-code (abi/screenshot-to-code, 72.5k★)** — github.com/abi/screenshot-to-code
- **ksnip (ksnip/ksnip, 3.2k★)** — Quick-Mode skip-editor (#968), camera shutter sound (#962), number tool (#982) — github.com/ksnip/ksnip
- **Captura (MathewSachin/Captura, 10.7k★)** — abandoned (last stable 2018) — github.com/MathewSachin/Captura
- **Screenity (alyssaxuu/screenity, 18.2k★)** — keyboard-shortcuts-on-screen during record (#248) — github.com/alyssaxuu/screenity

#### S6 commercial — paid competitor 2025-2026 changelog
- **Snagit Windows 2026 version history** — support.techsmith.com/hc/en-us/articles/42674936732685-Snagit-Windows-2026-Version-History (Step→PowerPoint/Word, Smart Move, Smart Redact, OCR, editable cursor)
- **TechSmith subscription transition** — support.techsmith.com/hc/en-us/articles/27009223314701 (annual subscription only since 2025, Snagit 2024 perpetual end Dec 2026 / Oct 2027)
- **CleanShot X changelog** — cleanshot.com/changelog (Mac-only; Cloud SSO/SCIM/2FA/branded-sharing; transcripts pulled-in-front-of-paywall)
- **Roundfleet "CleanShot X for Windows"** — roundfleet.com/library/cleanshot-x — confirmation no Windows port
- **Shottr** — shottr.cc/newversion.html (OCR remove-linebreaks; APCA contrast; expandable background; raster)
- **Snipaste PRO Wiki** — github.com/Snipaste/feedback/wiki/PRO (paywall list); Snipaste Changelog Wiki — github.com/Snipaste/feedback/wiki/Changelog
- **PicPick changelog** — picpick.app/update/ (7.4.1 Nov-2025 bug-fix; layer support + WebP in beta)
- **FastStone Capture version history** — videohelp.com/software/FastStone-Capture-/version-history (11.2 Nov-2025 minor; 11.1 Oct-2025 modernized dialogs; 11.0 Jan-2025)
- **Zight plans** — zight.com/plans/ (Smart Actions LLM pipeline + sharing analytics is the actual product, not the capture engine)
- **Lightshot enterprise block** — econnection.mst.edu/2025/08/lightshot-screen-capture-app-blocked-due-to-cybersecurity-vulnerability/ (Aug 2025)
- **Lightshot still broken on Win11+macOS** — peaklinesoftware.github.io/lightshot-alternative/ (v7.0.1 Jul-2024, no fix in 22 months)
- **Snipping Tool wave updates** — blogs.windows.com/windows-insider/2025/05/22/paint-snipping-tool-and-notepad-updates-with-new-features-begin-rolling-out-to-windows-insiders/
- **Snipping Tool gains AI Perfect Screenshot** — windowscentral.com/microsoft/windows-11/windows-11-just-got-a-wave-of-ai-features-one-snipping-tool-addition-makes-me-want-a-copilot-pc
- **Snipping Tool on-device OCR** — windowsnews.ai/article/windows-11-snipping-tool-gains-on-device-ocr-text-extraction-and-qr-code-scanning-in-latest-update.405061
- **Snipping Tool Capture-Bar OCR** — windowslatest.com/2025/10/29/windows-11-ai-feature-lets-you-copy-texts-from-screen-images-or-pdfs-now-rolling-out/
- **Microsoft pulling Copilot branding** — windowsnews.ai/article/microsoft-removes-copilot-branding-in-windows-11what-changes-for-it-and-users.416697
- **Tella vs Loom** — albertodirisio.com/tella-vs-loom/, shannahalbert.com/blog/loom-vs-tella, supademo.com/blog/loom-pricing
- **ShareX 20 ARM64 store launch** — windowsforum.com/threads/sharex-20-update-brings-avalonia-image-editor-arm64-store-support-ai-improvements.416253/, alternativeto.net/news/2026/4/sharex-20-0-released-with-native-arm64-support-via-ms-store-and-modernized-image-editor/, neowin.net/news/my-favorite-screenshot-taking-app-for-windows-updated-with-a-reworked-image-editor-and-more/

#### S6 platform — Windows capture API + WindowsAppSDK + signing
- Windows.Graphics.Capture WinRT-26100 reference — learn.microsoft.com/en-us/uwp/api/windows.graphics.capture?view=winrt-26100
- TryCreateFromDisplayId — learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscaptureitem.trycreatefromdisplayid?view=winrt-26100
- TryCreateFromWindowId — learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscaptureitem.trycreatefromwindowid?view=winrt-26100
- Screen capture UWP — learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture
- robmikh/Win32CaptureSample — github.com/robmikh/Win32CaptureSample (canonical WGC sample, FP16/HDR paths)
- Chromium HDR fix on 24H2 (DirtyRegionMode + FP16) — windowslatest.com/2025/04/19/microsoft-fixes-chromes-washed-out-dull-hdr-colours-on-windows-11-24h2/
- DirectX HDR — learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range
- DirectXTK12 HDR rendering wiki — github.com/microsoft/DirectXTK12/wiki/Using-HDR-rendering
- Direct3D11CaptureFramePool.Create — github.com/MicrosoftDocs/winrt-api/blob/docs/windows.graphics.capture/direct3d11captureframepool_create_238466248.md
- D3D12 AV1 Video Encoding (WDDM 3.2 / Win11 24H2) — learn.microsoft.com/en-us/windows-hardware/drivers/display/video-encoding-d3d12-av1
- D3D12 Video Encoding spec — microsoft.github.io/DirectX-Specs/d3d/D3D12VideoEncoding.html
- AMD AMF AV1 Encoder Wiki — github.com/GPUOpen-LibrariesAndSDKs/AMF/wiki/AV1-Encoder
- WinAppSDK 1.7 release notes (ImageScaler + Object Erase + Image Segmentation) — learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-7
- WinAppSDK 1.8 release notes (Microsoft.Windows.Storage.Pickers, TextRewriter/TextSummarizer) — learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-8
- WinUI vs WPF in 2026 — ctco.blog/posts/winui-vs-wpf-2026-practical-comparison/
- Build 2025 Native Windows app announcements — thurrott.com/dev/321124/build-2025-microsoft-announces-new-capabilities-for-native-windows-apps
- Windows Studio Effects overview (now on external cameras 26100.7309 / 26200.7309) — learn.microsoft.com/en-us/windows/apps/develop/windows-integration/studio-effects
- Windows Studio Effects application card — learn.microsoft.com/en-us/windows/ai/cards/windows-studio-effects-application-card
- Windows AI Foundry TextRecognizer — windowsnews.ai (multiple articles 2025-10)
- PowerToys Text Extractor — learn.microsoft.com/en-us/windows/powertoys/text-extractor
- Phone Link Task Continuity (no public capture API) — learn.microsoft.com/en-us/windows/cross-device/phonelink/
- Phone Link mirror Jan-2026 update — windowslatest.com/2026/01/07/windows-11s-almost-full-screen-android-apps-mirroring-now-available-for-everyone-via-phone-link-app-with-supported-phones/
- Phone Link remote-lock Dec-2025 — windowslatest.com/2025/12/17/you-can-now-lock-windows-11-from-android-remotely-send-files-to-pc-share-clipboard-mirror-screen-and-more/
- DXcam (D3DShot successor) — github.com/ra1nty/DXcam
- OBS WGC vs DXGI — obsproject.com/forum/threads/windows-graphics-capture-vs-dxgi-desktop-duplication.149320/, github.com/obsproject/obs-studio/discussions/11486
- Insider build May-2026 (28020 + ISOs) — blogs.windows.com/windows-insider/2026/05/01/announcing-new-builds-for-1-may-2026-and-extending-iso-support/
- 26H1 catch-up — windowscentral.com/microsoft/windows-11/windows-11-version-26h1-plays-catch-up-with-new-features-brought-over-from-version-25h2
- 26H1 build 28020.1362 — neowin.net/news/windows-11-26h1-gets-big-update-with-a-lot-of-new-features-in-build-280201362/
- Azure Artifact Signing (formerly Trusted Signing, GA Apr-2026, $9.99/mo) — azure.microsoft.com/en-us/products/artifact-signing, learn.microsoft.com/en-us/azure/artifact-signing/faq, securityboulevard.com/2026/01/how-to-set-up-azure-trusted-signing-to-sign-an-exe/, melatonin.dev/blog/code-signing-on-windows-with-azure-trusted-signing/
- Code-signing options — learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options
- winget schema 1.9.0 — github.com/microsoft/winget-pkgs/blob/master/doc/manifest/schema/1.9.0/README.md
- winget 1.7 tracking — github.com/microsoft/winget-cli/issues/3568
- winget manifest doc — learn.microsoft.com/en-us/windows/package-manager/package/manifest
- HDR JXR screenshots — robertsspaceindustries.com/community-hub/post/how-to-take-hdr-screenshots-jxr-image-format-w1SHvjA3prbmL, neowin.net/news/windows-11-now-supports-hdr-wallpapers-in-jxr-format/, elevenforum.com/t/enable-or-disable-hdr-screenshot-color-corrector-in-snipping-tool-in-windows-11.24243/, nelsonslog.wordpress.com/2026/01/01/hdr-screenshots-on-windows/

#### S6 community — pain points + format trends 2025-2026
- Win11 25H2 Snipping Tool hotkey break — windowsforum.com/threads/windows-25h2-breaks-snipping-tool-hotkey.387895/
- Jan-2026 KB5074109 broke Notepad/Snipping Tool — neowin.net/news/notepad-snipping-tool-other-apps-broken-by-new-bug-in-windows-11/, winbuzzer.com/2026/01/22/windows-11-january-update-breaks-notepad-snipping-tool-and-other-apps-xcxwbn/, windowscentral.com/microsoft/windows-11/another-windows-11-issue-breaks-apps-and-causes-crashes-and-im-not-talking-about-the-error-code-0x803f8001
- Multi-monitor mixed-DPI broken on 26100 — learn.microsoft.com/en-us/answers/questions/4095392/multiple-monitor-snipping-tool-scaling-still-broke?forum=windows-all, learn.microsoft.com/en-us/answers/questions/3922935/snipping-tool-not-working-with-aspect-ratio-wrong
- PrintScreen 24H2 reclaim — github.com/flameshot-org/flameshot/discussions/3465, learn.microsoft.com/en-us/answers/questions/4065953/changing-the-function-of-the-printscreen-button
- Skitch successor 2026 — dev.to/tommy_worklab/the-day-skitch-stopped-working-which-screenshot-annotation-tool-should-you-use-in-2026-bm1
- WinShot Wails 10MB — dev.to/githubopensource/ditch-the-bloat-winshot-delivers-native-speed-and-pro-annotations-in-a-10mb-screenshot-tool-40hm
- ShareX 20 ARM64 — windowsforum.com/threads/sharex-20-update-brings-avalonia-image-editor-arm64-store-support-ai-improvements.416253/
- Windows screenshot tools 2026 — pixeltaken.com/2025/11/12/windows-screenshot-tools-in-2026-whats-new-and-what-still-needs-fixing/
- AlternativeTo: Greenshot 2026 — alternativeto.net/software/greenshot/, Flameshot 2026 — alternativeto.net/software/flameshot/, Lightshot 2026 — alternativeto.net/software/lightshot/
- HN thread Windows 2025 screenshot tools — news.ycombinator.com/item?id=44795542
- JPEG XL Wikipedia — en.wikipedia.org/wiki/JPEG_XL
- Chromium 145 JXL reversal — winbuzzer.com/2025/11/23/chrome-browser-google-to-reintroduce-jpeg-xl-image-format-support-xcxwbn/, devclass.com/2025/11/24/googles-chromium-team-decides-it-will-add-jpeg-xl-support-reverses-obsolete-declaration/
- PDF Association picks JXL — theregister.com/2025/11/10/another_chance_for_jpeg_xl/
- JXL caniuse — caniuse.com/jpegxl
- AVIF Wikipedia — en.wikipedia.org/wiki/AVIF
- Windows.Media.Ocr complete guide — copyprogramming.com/howto/windows-media-ocr-namespace
- Win2D documentation — microsoft.github.io/Win2D/WinUI2/html/Introduction.htm

#### S6 dep — dependency health (CVE + version)
- SkiaSharp 3.119.2 NuGet — nuget.org/packages/SkiaSharp; 3.x breaking changes — github.com/mono/SkiaSharp/issues/2544; SkiaSharp.Views.WPF NU1701 — github.com/mono/SkiaSharp/issues/3316
- CommunityToolkit/dotnet releases — github.com/CommunityToolkit/dotnet/releases (8.4.2 current, no 9.0 yet); 8.4.0 .NET 10 #1139 — github.com/CommunityToolkit/dotnet/issues/1139
- Velopack — nuget.org/packages/velopack, github.com/velopack/velopack (.NET 10 file-app-directive support)
- Hardcodet NotifyIcon NuGet — nuget.org/packages/Hardcodet.NotifyIcon.Wpf/; H.NotifyIcon active fork — github.com/HavenDV/H.NotifyIcon
- RapidOCR.Net — nuget.org/packages/RapidOCR.Net; RapidOcrOnnx — github.com/RapidAI/RapidOcrOnnx (DirectML rough Mar 2025)
- OpenCvSharp4 NuGet — nuget.org/packages/OpenCvSharp4; OpenCV 4.13 stable; CVE-2025-53644 fixed at 4.12.0 — nvd.nist.gov/vuln/detail/cve-2025-53644

#### S6 sec — security advisories
- **GDI+ CVE-2025-30388 (May), CVE-2025-47984 (Jul), CVE-2025-53766 (Aug, CVSS 9.8 RCE)** — nvd.nist.gov/vuln/detail/CVE-2025-53766; Check Point research — research.checkpoint.com/2025/drawn-to-danger-windows-graphics-vulnerabilities-lead-to-remote-code-execution-and-memory-exposure/; Rapid7 Aug-2025 PT — rapid7.com/blog/post/patch-tuesday-august-2025/
- **SQLite CVE-2025-6965 (CVSS up to 9.8)** — nvd.nist.gov/vuln/detail/CVE-2025-6965; Broadcom advisory — knowledge.broadcom.com/external/article/405851/sqlite-vulnerability-cve20256965.html; **CVE-2025-29088** — system.data.sqlite.org/home/info/e97c7438350c1c95a580a8ab16ef1d640c51bcd7
- **Magick.NET 14.12.0 floor** — CVE-2025-57803 (BMP int-overflow CVSS 9.8), CVE-2026-33902 (FX expression stack overflow), CVE-2026-33901 (MVG decoder heap overflow), CVE-2026-34238 (despeckle int-overflow), CVE-2026-25983 (MSL UAF) — gbhackers.com/critical-imagemagick-vulnerability/, sentinelone.com/vulnerability-database/cve-2026-33901/, advisories.gitlab.com/nuget/magick.net-q8-anycpu/CVE-2026-33902/
- **OpenCvSharp4 4.12.0 floor** for CVE-2025-53644 — nvd.nist.gov/vuln/detail/cve-2025-53644

#### S6 adjacent — non-screenshot tools to steal from
- **Excalidraw+ Changelog** (Oct 2025: push-to-talk + QR-code session sharing) — plus.excalidraw.com/changelog
- **Excalidraw 2024 wrap** (Mermaid paste, etc.) — plus.excalidraw.com/blog/excalidraw-in-2024
- **tldraw make real** — makereal.tldraw.com/, computer.tldraw.com/, tldraw.substack.com/p/make-real-the-story-so-far
- **Snappify line-state markers** — snappify.com/blog/best-ray-so-alternatives, snappify.com/blog/carbon-now-sh-alternatives, snappify.com/blog/best-screenshot-tools
- **Scribe BPMN** — scribe.launchnotes.io/
- **Tella zoom + auto-features** — tella.com/features/zoom, efficient.app/compare/tella-vs-screen-studio
- **Eagle pin-on-top** — eagle.cool/blog/post/eagle-image-pin-on-top-for-designers
- **PureRef alternatives 2026** — kosmik.app/blog/pureref-alternatives, syncwin.com/pureref-vs-eagle/
- **PowerToys 0.99 (Foundry Local + Ollama in Advanced Paste; ZoomIt; PowerRename EXIF tokens)** — devblogs.microsoft.com/commandline/powertoys-0-99-is-here-new-monitor-controls-easier-window-management-and-dock-upgrades/, neowin.net/news/powertoys-096-is-out-with-new-features-to-command-palette-light-switch-and-other-modules/, alternativeto.net/news/2025/1/powertoys-0-88-adds-a-new-zoomit-utility-for-screen-zooming-annotation-and-recording/
- **Raycast Storage Duration auto-cleanup** — raycast.com/resources, raycast.com/dimuuu/haystack
- **Lazy Screenshots clipboard workflow** — lazyscreenshots.com/blog/screenshot-clipboard-workflow-mac/
- **Notion image management** — notionry.com/faq/how-to-add-and-manage-images-in-notion
- **Obsidian Web Clipper screenshot pain** — forum.obsidian.md/t/webclipper-screenshot/96741
- **Joplin Web Clipper** — joplinapp.org/help/apps/clipper/

### S6+9d — 2026-05-09 → 2026-05-17 delta dossier

#### S6+9d sec — security advisories disclosed inside the window
- **CVE-2026-40403 (CVSS 9.8)** — Win32K-GFX heap overflow RCE via crafted font/image rendering. Affects GDI/Magnification API path. Patched May 2026 Patch Tuesday. — windowsnews.ai/article/cve-2026-40403-win32k-grfx-rce-patch-the-may-2026-windows-graphics-bug.417978
- **CVE-2026-35421** — Windows GDI EMF heap overflow RCE; affects GDI+/System.Drawing.Common decode path used by clipboard EMF paste. Patched May 2026. — blog.talosintelligence.com/microsoft-patch-tuesday-may-2026/
- **CVE-2026-32175** — .NET tampering (arbitrary file write) for .NET 8/9/10. Patched in 10.0.8 servicing release 2026-05-12. — github.com/dotnet/announcements/issues/396
- **CVE-2026-32177 / 35433 / 42899** — .NET EoP + DoS bundled with 10.0.8. — devblogs.microsoft.com/dotnet/dotnet-and-dotnet-framework-may-2026-servicing-updates/
- **CVE-2026-33829** (disclosed April 2026, PoC Apr-22; active discussion in window) — Snipping Tool NTLMv2 hash leak via `ms-screensketch://filePath=<UNC>` deep-link triggering transparent SMB authentication. CVSS 4.3 information disclosure (CWE-200). Affects all Windows 10/11/Server pre-April-2026 patch. — socprime.com/active-threats/cve-2026-33829-snipping-tool-ntlm-leak/, exploit-db.com/exploits/52567, cryptika.com/poc-exploit-released-for-windows-snipping-tool-ntlm-hash-leak-vulnerability/
- **May 2026 Patch Tuesday roundup** — bleepingcomputer.com/news/microsoft/microsoft-may-2026-patch-tuesday-fixes-120-flaws-no-zero-days/

#### S6+9d platform — Microsoft platform deltas inside the window
- **KB5089548 (OS Build 28000.2113, 2026-05-12)** — Windows 11 26H1 GA cumulative for Copilot+ hardware-only branch. Narrator now ships "rich image descriptions" via Copilot integration (Narrator + Ctrl + D for focused image, Narrator + Ctrl + S for full screen). — windowsnews.ai/article/windows-11-26h1-kb5089548-hardware-only-branch-not-an-upgrade-for-existing-pcs.418200, pureinfotech.com/kb5083806-windows-11-26h1-may-2026-update/
- **KB5089549 (OS Builds 26100.8457 + 26200.8457, 2026-05-12)** — 24H2 + 25H2 cumulative. Refreshes built-in AI sub-components (Image Search, Content Extraction, Semantic Analysis, Settings Model → 1.2604.515.0). Same model surface `Microsoft.Windows.AI.Imaging` binds to. — support.microsoft.com/en-us/topic/may-12-2026-kb5089549-os-builds-26200-8457-and-26100-8457-28ec2a99-4bbe-481d-a340-5c6cf18d9acb
- **KB5089573 (Release Preview, 2026-05-14)** — 24H2/25H2 builds 26100.8514 / 26200.8514. Begins AI actions in File Explorer (image edit / doc summarize). — elevenforum.com/t/kb5089573-windows-11-insider-release-preview-build-26100-8514-24h2-and-26200-8514-25h2-may-14.46833/
- **KB5089570 (Release Preview, 2026-05-14)** — 26H1 build 28000.2173. First 26H1 RP flight; NPU task-management improvements that rework CPU/NPU dispatch for AI Imaging APIs. — elevenforum.com/t/kb5089570-windows-11-insider-release-preview-build-28000-2173-26h1-may-14.46834/
- **Windows App SDK 1.8.8 / 1.8.260508005 (2026-05-12)** — Servicing: XAML Islands popup `OverrideScale` clipping fix; `RenderTargetBitmap.RenderAsync` ACCESS_VIOLATION fix when target leaves visual tree; `GetReadyState` returns correct `NotReady` when Windows AI packages missing. — github.com/microsoft/WindowsAppSDK/discussions/6475
- **.NET 10.0.8 (2026-05-12)** — Runtime baseline. — devblogs.microsoft.com/dotnet/dotnet-and-dotnet-framework-may-2026-servicing-updates/
- **Microsoft Build 2026 — June 2-3, San Francisco, Fort Mason** (NOT in window). No capture/imaging announcements leaked in delta window; session catalog live but no Windows-AI capture sessions. Re-run research after Build. — thurrott.com/microsoft/334704/microsofts-build-2026-session-catalog-is-now-live
- **Windows Insider Dev — Build 15-May-2026** — Taskbar / Widgets / Search only; no screenshot relevance. — blogs.windows.com/windows-insider/2026/05/15/announcing-new-builds-for-15-may-2026/
- **"Ask Copilot" removed from Snipping Tool + Photos (Insider rollout, 2026-05-08)** — Microsoft narrowing Snipping Tool's AI surface. — blogs.windows.com/windows-insider/2026/05/08/announcing-new-builds-for-8-may-2026/
- **Snipping Tool "Perfect Screenshot" + native Color Picker in Insider 11.2504.38.0+** — laptopmag.com/laptops/windows-laptops/windows-11-snipping-tool-color-picker
- **JPEG XL in Chrome 145 stable** — flag-gated `chrome://flags/#enable-jxl-image-format`, default-enabled expected H2 2026; Google switched to Rust-based `jxl-rs` decoder for memory safety. — phoronix.com/news/Chrome-145-Released, mochify.app/guides/chrome-145-jpeg-xl-default

#### S6+9d OSS — release deltas inside the window
- **Peekaboo v3.0 / v3.1.0 / v3.1.1 / v3.1.2 / v3.2.0 (2026-05-09 → 2026-05-15)** — macOS screenshot CLI + MCP server. v3.0 unified screenshot + UI detection; v3.1.0 daemon-backed lightweight-metadata returns; v3.2.0 `ollama/<model>` + `lmstudio/<model>` provider syntax. — github.com/openclaw/Peekaboo/releases
- **Screenity v4.4.0 → v4.4.7 (2026-05-09 → 17)** — Chrome ext. v4.4.0 WebCodecs encoder + OPFS direct-to-disk + auto-update deferral; v4.4.1 MediaRecorder fallback; v4.4.2-5 editor routing / recovery / Linux Chrome WebCodecs / BT-USB headphone fixes; v4.4.7 back-to-back-recording + cloud-upload retry. — github.com/alyssaxuu/screenity/releases
- **Greenshot 1.4.187 / 1.4.188 / 1.4.189 / 1.4.190 / 1.4.191 (2026-05-13 / 14)** — continuous pre-releases; capture-correctness + startup-crash + DE/IT/FR translations. — github.com/greenshot/greenshot/releases
- **Cap v0.4.86 / v0.4.87 (2026-05-16)** — release-pipeline hotfix. — github.com/CapSoftware/Cap/releases/tag/cap-v0.4.87
- **0PandaDEV/awesome-windows commits (2026-05-15 / 16)** — actively maintained; Snapture not yet submitted. — github.com/0pandadev/awesome-windows/commits/main
- **No delta in window:** ShareX (last v20.1.0 May 6), Flameshot (v14.0 RC1), ScreenToGif (2.43.1 Mar 22), openscreen (v1.4.0 May 6), snow-shot (18-month tag silence — abandonment watch), eSearch (v15.2.3 Jan 4), SnapX (v0.4.0 Feb 20), WinShot, Capter, NormCap, wcap, xcap (v0.9.4 Apr 9), OBS Studio (32.1.2 Apr 21), screenshot-to-code (commits but no tag), ksnip, Captura.

#### S6+9d adjacent — adjacent / new entrants inside the window
- **Raycast 2.0 Windows Public Beta (2026-05-14)** — first cross-OS Raycast. — alternativeto.net/news/2026/5/raycast-launches-public-beta-with-new-ui-search-dictation-and-ai-upgrades/
- **Raycast Windows Changelog v0.58 / v0.59 (2026-05-12 / 13)** — "Scan sub-directories for screenshots" treating screenshot folders as a first-class search index; "Save as File" action for clipboard images. — raycast.com/changelog/windows
- **Adjacent quiet:** PowerToys (still 0.99); Excalidraw / tldraw / Snappify / PureRef / Eagle — no releases in window.

#### S6+9d community — pain points + roundups inside the window
- **Snipping Tool window-position-drift bug** — successive captures drift the window upward off-screen. — windows11forums.com/threads/new-snipping-tool-in-windows-11-is-complete-and-absolute-garbage.1344/
- **Snipping Tool loses focus behind other windows post-capture** — workflow tax noted in elevenforum threads.
- **Multi-monitor scaling broken on RDP-style dialogs (May 2026 Patch Tuesday acknowledgement)** — windowsforum.com/threads/patch-now-may-2026-patch-tuesday-fixes-critical-dns-and-netlogon-flaws.418160/
- **Skitch successor 2026 cohort** — dev.to/tommy_worklab/the-day-skitch-stopped-working-which-screenshot-annotation-tool-should-you-use-in-2026-bm1
- **No major HN / Reddit / Lobsters thread in window** — front-page checks for 2026-05-11 to 2026-05-16 surfaced nothing screenshot-specific. — news.ycombinator.com/front?day=2026-05-11

#### S6+9d sec — secondary refs
- **PoC + writeup for CVE-2026-33829** — exploit-db.com/exploits/52567, technochat.in/how-the-windows-snipping-tools-cve-2026-33829-opens-the-door-to-ntlm-hash-theft/, cyberpress.org/windows-snipping-tool-vulnerability/
- **CVE-2026-41096 Windows DNS Client heap overflow (CVSS 9.8)** + **CVE-2026-34329 MSMQ heap overflow** — both bundled in KB5089549 prereq, not Snapture-direct deps but documentation hooks. — bleepingcomputer.com/news/microsoft/microsoft-may-2026-patch-tuesday-fixes-120-flaws-no-zero-days/
```

</details>
