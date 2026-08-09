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
  <img alt="Version"  src="https://img.shields.io/badge/version-0.8.1-CBA6F7?style=for-the-badge">
  <img alt="License"  src="https://img.shields.io/badge/license-MIT-A6E3A1?style=for-the-badge">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2010%2F11-89B4FA?style=for-the-badge&logo=windows&logoColor=white">
  <img alt=".NET 10"  src="https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white">
  <img alt="Downloads" src="https://img.shields.io/github/downloads/SysAdminDoc/Snapture/total?style=for-the-badge&color=89DCEB&label=downloads">
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

**Snapture's pitch:** the polish of Snagit, the no-cloud philosophy of Greenshot, modern WinRT-class capture, and a Catppuccin-themed editor that doesn't look like it was designed in 2008.

**Auto-redact:** Snipping Tool's "Quick Redact" catches 2 things (phones and emails). Snapture catches 30+ — AWS keys, GCP tokens, GitHub PATs, Stripe keys, Slack webhooks, JWTs, npm tokens, credit cards (Luhn-validated), SSNs, IBANs, IPs, MACs, HIPAA identifiers, and more. All local, all undoable.

## What ships in v0.8.1

- **Security and release integrity** — loopback MCP bearer authentication, bounded image intake, an executable outbound-data-flow inventory, exact plugin artifact approval, offline SBOM/security-floor verification, recoverable ring-buffer recording, bounded local-AI contracts, explicit export metadata policy, and documentation drift checks.
- **Dual-architecture distribution** — unsigned x64 and ARM64 portable/release paths, with portable mode keeping settings, history, plugins, logs, autosave, and crash data under `SnaptureData`.

- **Sticky-header / sticky-footer detection** in the image stitcher — UI chrome that doesn't scroll appears once at the top + bottom of the stitched output instead of repeating per frame.
- **Animated GIF recording** — Tray → Tools → Record GIF (foreground window or all monitors). 10 fps default, frames held in memory until you stop, saved through a standard file dialog.
- **Crash-recoverable ring-buffer recording** — the rolling MP4 uses atomic session manifests and three bounded 30-second segments, retains only the last 90 seconds, quarantines interrupted fragmented media for explicit review, and reports discarded stale/corrupt data without opening it automatically.
- **GIF clip editing** — Tray → Tools → Edit GIF opens an existing animation for frame deletion and editing; deletion-only changes can be saved as a lossless clip that keeps the source palettes and compressed frame data.
- **Per-rule auto-redact toggles** — Settings → Auto-redact lists every detector rule as a checkbox. Disabled set is persisted; new rules ship enabled.
- **Plugin contract resize support** — capture processors may now resize, not just replace pixels. Plugin output lands in the saved file and the history index (the order is now capture → plugins → save).
- **Local batch image processing** — Tray → Tools → Batch process images applies resize, border, watermark, and PNG/JPG/BMP/WebP conversion to a local folder with bounded file counts and per-file failure reporting.
- **Image combiner** — Tray → Tools → Combine images joins two to 100 local stills vertically, horizontally, or as a configurable grid with gap controls and PNG/JPG/BMP/WebP export.
- **Before/after comparison GIFs** — Tray → Tools → Before/after GIF creates a bounded local ping-pong cross-fade animation from two still images.
- **Code-aware export** — Tray → Tools → Code-aware export uses local OCR plus code/monospace heuristics, then renders syntax-highlighted text with the existing gradient, drop-shadow, and code-window chrome.
- **Explicit export metadata policy** — Settings → Output and the Export dialog independently control ordinary source metadata (`strip`, `preserve`, or replace), ICC behavior (`strip`, preserve source, or embed a single-monitor display profile), and an opt-in descriptive `.provenance.json` sidecar. Redacted exports suppress prohibited source metadata; composite captures report that no single display ICC applies. The sidecar is local inspection data, not a signed C2PA assertion. Headless capture and conversion expose the same choices through `--metadata`, `--icc`, and `--provenance`.
- **Edge-detection ruler** — Pixel ruler Alt+click finds the nearest high-contrast UI edge in a frozen local screen sample while preserving the regular distance-and-angle measurement mode.
- **Pinned comparison boards** — select pins with Ctrl+click, then open a board for vertical, horizontal, or grid snap-arrangement; named layout presets persist locally without duplicating image pixels.
- **Omnidirectional scrolling capture** — tray actions cover vertical, horizontal, and two-axis UIA scroll hosts, placing captured tiles into a bounded stitched canvas.
- **Polished desktop UX** — shared WPF chrome, calmer settings/editor/history surfaces, explicit empty states, refined capture picker and recording HUDs, consistent overlay guidance, safer pinned captures, and dark-theme controls across the app.
- **`docs/HOTKEYS.md`** + **`docs/CAPTURE-MATRIX.md`** + **`manifests/`** for winget submission.

## What shipped in v0.5.0

- **Image-stitch fallback for scrolling capture** — pure-managed subsampled-SAD seam alignment. Browser pages that fell through to "no UIA scroll" in v0.3 now stack cleanly.
- **Code-window chrome** — Carbon-style export wrapper with macOS traffic-light dots, dark titlebar, rounded corners. Pairs with the existing drop-shadow / gradient frame wrappers.

## What ships in v0.4.0

**The differentiator wave**

- **Auto-redact secrets** — Editor button runs OCR + a Gitleaks-derived rule pack (AWS, GCP, GitHub, Stripe, Slack, JWT, npm) plus PII (Luhn-validated cards, SSN, IBAN, IPs, MACs, emails) and drops solid-fill redactions on every match. Each redaction is its own undo-stack entry so false positives are easy to back out.
- **LAN-only share server** — Local Kestrel server, binds to a single user-chosen adapter (never `0.0.0.0`), serves single-fetch token URLs that expire after the TTL. Settings tab to toggle / configure / inspect; editor button to share the current document.
- **Smart Element Capture** — Non-activating overlay highlights individual UIA controls in real time. PgUp climbs the parent chain, click captures the exact element rectangle. The capability no consumer screenshot tool currently ships during capture.
- **Plugin SDK** — `Snapture.Plugin.Abstractions` ships as a separate multi-target library. Plugins can be installed or updated from Settings/Tools → Plugins, load in collectible `AssemblyLoadContext`s, declare capabilities via `[SnapturePlugin]`, and can declare inclusive `minHostVersion` / `maxHostVersion` bounds. Incompatible or malformed ranges are rejected before plugin constructors run. The SDK includes `ICaptureSource` for camera, scanner, file-watch, and other local source roles; source responses are metadata-first with explicit pixel opt-in. The Plugins window separately approves the exact DLL SHA-256 artifact and its declared capabilities, invalidating trust when the artifact or version changes; the UI clearly states that collectible in-process loading is not an OS sandbox. It then supports install/update, uninstall, reload, optional JSON configuration through `IPluginConfigurable`, per-plugin encrypted credentials through `IPluginSecretStore`, and on-demand pinned tool downloads through `IPluginDependencyStore`. Secret and dependency stores use per-plugin current-user data roots; secrets use Windows DPAPI and dependency artifacts require HTTPS plus SHA-256 verification.
- **External command destination** — Settings → Output → Configure commands stores local CLI profiles that receive the flattened editor PNG through stdin or a temporary `{file}` argument. Use `{source}`, `{width}`, `{height}`, and `{timestamp}` for capture metadata. Commands are launched directly with shell execution disabled, require an explicit editor/tray action, and expose bounded stdout/stderr for local diagnostics.
- **Declarative uploader profiles** — Settings → Output imports user-owned ShareX `.sxcu` or compatible JSON files. Multipart, form-urlencoded, JSON, XML, binary, query, header, and response JSON-path fields are supported; imported endpoints stay inert until an explicit editor/tray upload action.
- **Self-hosted destinations** — Settings → Output can opt into built-in Nextcloud WebDAV and Immich destinations. Both are disabled by default; Nextcloud app passwords and Immich API keys use the current-user DPAPI secret store and are never serialized into `settings.json`.

## Localization foundation

The WPF surface uses an embedded `.resx` catalog with deterministic keys, system-culture fallback, and load-time coverage for titles, controls, tooltips, headers, and accessibility names. Reviewed satellite resources can be added without rewriting views.

Settings → Output can also opt into a local watch folder that indexes completed image drops into History without opening or uploading them. Stable-write detection plus content-aware signature, decoder, and resource-limit checks keep partial, malformed, and oversized files out of the index.

## What ships in v0.3.0

**Capture parity pass — OCR, full-text searchable history, scrolling capture (alpha)**

- **Built-in OCR** — Windows AI `TextRecognizer` when its local model is ready, `Windows.Media.Ocr` as the Windows fallback, and bundled local RapidOCR PP-OCRv5 Latin models when neither Windows engine can return text. An optional user-supplied `sponeocr.exe` sidecar can use the Windows Snipping Tool's local OneOCR model without cloud traffic. Tray → Tools → "OCR region…" picks a region, recognised text lands in the clipboard, result window opens. The History window's "OCR all" button bulk-indexes everything past captures into FTS5.
- **OCR text overlays** — Editor → **OCR overlay** turns positioned OCR lines into editable, contrast-aware text annotations anchored to their image regions. The operation is one undoable batch and text-only engines decline cleanly when no geometry is available.
- **OCR table mode** — Editor → **Table** reconstructs rows and columns from positioned OCR word boxes and copies the result as tab-separated values. Text-only engines decline cleanly instead of guessing columns.
- **Step Capture mode** — Records key chords and cursor clicks, snapshots the foreground window, presents a review window with per-step input tracks and captions, and exports Markdown, editable DOCX, or title-plus-step PPTX bundles. The Snagit single-vendor feature with no OSS equivalent.
- **QR and barcode extraction** — Editor → **Codes** runs a local ZXing.Net pass across QR, Data Matrix, Aztec, PDF417, Code 128, EAN/UPC, and other common formats, then lists payloads and image regions for copying.
- **Capture presets** — Bug-report, Code-block, Documentation, and Quick-share-LAN templates are available in Settings and the tray, with editable local output, naming, cursor, editor, and LAN-share choices.
- **Per-app capture profiles** — Settings → Output maps Win32 foreground window class names such as `Chrome_WidgetWin_1` or `Notepad` to those presets, applying the matching output and delivery choices automatically at capture time.
- **LAN-share QR overlay** — Editor → Share to LAN opens a locally generated QR image for the expiring single-fetch URL, so a phone on the same Wi-Fi can scan instead of typing.
- **Capture-safe desktop icons** — Settings can temporarily hide the Windows desktop icon list before a capture and restore it even when the capture path fails.
- **Capture history** with **SQLite + FTS5** at `%LOCALAPPDATA%\Snapture\history\index.db`. Every capture auto-tagged with foreground process + window title, plus local named projects, dominant-color signatures, perceptual hashes, and `Verified-redacted` status for saved Auto-redact results. The History window supports extended multi-select assignment without moving original files, color/duplicate/verification filters, `.snapture-library` backup/restore, and FTS5 search across OCR text, process name, and window title. Right-click → Open in editor / Pin / Run OCR / Reveal / Delete.
- **Scrolling capture (alpha)** via UIA `IScrollProvider`. Native scroll panes (Office side-panes, Explorer, WPF/WinForms apps) work; browsers fall through to a clear "this window doesn't expose UIA scroll" message. Image-stitching for browsers ships in v0.4.

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
- **Pin window polish** — opacity submenu (25/50/75/100% plus Ctrl-scroll), border / shadow toggles, `Alt+click` click-through, `O` solo mode, `H` hide/show all pins, and Ctrl+click selection with group drag, opacity, and close controls.

## What's coming (selected)

See [ROADMAP.md](ROADMAP.md) for the full picture.

- **Post-v0.7 follow-up** — v0.7.0 shipped MP4 / HEVC / AV1 recording with fragmented MP4, hardware encoder discovery, dirty-region skips, system-audio / app-only audio / mic capture, live VU meters, cursor/click effects, and a keystroke overlay. HDR tonemap (ACES) plus AVIF / JPEG XR remain deferred follow-up work. The unsigned MSIX, Velopack, Chocolatey, and Scoop packaging paths are available for operator-controlled distribution.

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

Download the latest tagged release from [Releases](https://github.com/SysAdminDoc/Snapture/releases).

To produce an unsigned MSIX and a staged App Installer feed locally, run `pwsh -File build/build.ps1 -Configuration Release -Runtime win-x64 -Msix -RolloutRing canary`. The package is written under `publish/`; the build intentionally does not sign software. The generated package declares `runFullTrust` and the startup-task extension without requesting `broadFileSystemAccess`. For clean local removal, run `pwsh -File build/uninstall.ps1`; the cleanup window includes a **Keep my settings, history, and plugins** checkbox. Automation can use `-KeepSettings`, `-Quiet`, or the non-destructive `-WhatIf` switches.

To produce unsigned Velopack release assets, run `dotnet tool restore`, then `pwsh -File build/build.ps1 -Configuration Release -Runtime win-x64 -Velopack` (repeat with `win-arm64` for the ARM64 package). The assets under `publish/velopack/<runtime>/` include architecture-specific stable feeds (`win-x64-stable` or `win-arm64-stable`), full packages, setup executables, and portable archives; publish both directories' files to the same GitHub Release download root. Installed Velopack builds can check, download, and apply updates from the tray; unpackaged source builds retain the GitHub release-page fallback. Release signing is intentionally operator-controlled and is never performed by the build.

After both payloads are built, run `pwsh -File build/verify-release.ps1`. This offline release gate scans the actual x64 and ARM64 payload files, emits deterministic CycloneDX 1.5 SBOMs, artifact manifests, license inventories, and security-floor reports under `publish/sbom/<runtime>/`, and binds each SBOM to the manifest SHA-256. It fails on stale SQLite, Magick.NET/ImageMagick, Windows App SDK, ONNX Runtime, SkiaSharp, native codec, or .NET runtime versions. Use `pwsh -File build/verify-release.ps1 -SelfTest` to exercise the passing, stale-floor, and artifact-tamper cases without network access.

Before publishing documentation or package metadata, run `pwsh -File build/verify-docs.ps1`. This offline drift gate derives the project/package version, checks the current x64 and ARM64 winget/Scoop inputs, compares the architecture table with the project references, and verifies the README CLI and privacy boundary claims. Use `pwsh -File build/verify-docs.ps1 -SelfTest` to exercise the passing, version, package, architecture, CLI, network, and stale-claim cases without creating a Markdown artifact.

To produce an unsigned enterprise MSI and matching MST transform, run `dotnet tool restore`, then `pwsh -File build/build.ps1 -Configuration Release -Runtime win-x64 -Msi` (repeat with `win-arm64` for the ARM64 package). The output under `publish/msi/<runtime>/` contains a per-machine MSI, an `-enterprise.mst` transform that renames the Start Menu shortcut to `Snapture Enterprise`, and WiX verification artifacts. SCCM/GPO can install the base package silently with `msiexec /i Snapture-v<version>-win-x64.msi /qn /norestart`, or apply the transform with `msiexec /i Snapture-v<version>-win-x64.msi TRANSFORMS=Snapture-v<version>-win-x64-enterprise.mst /qn /norestart`. The MSI payload is framework-dependent, so deploy the .NET 10 Desktop Runtime through the enterprise baseline. Signing is intentionally omitted.

To produce Chocolatey packages for both architectures, run `pwsh -File build/build.ps1 -Configuration Release -Runtime win-x64 -Chocolatey`. The command builds the matching Velopack assets, then writes `publish/chocolatey/snapture.<version>.nupkg` and `publish/chocolatey/snapture.portable.<version>.nupkg`. `snapture` downloads the silent Velopack setup executable; `snapture.portable` downloads and extracts the portable archive. Both package scripts select x64 or ARM64 and verify SHA-256 before installing. Publish the generated packages to the Chocolatey feed only after the corresponding versioned GitHub Release assets are available.

To build the multi-target plugin SDK package locally, run `pwsh -File build/build.ps1 -Configuration Release -NuGet`. This writes and validates `Snapture.Plugin.Abstractions.<version>.nupkg` plus symbols under `publish/nuget/`; publishing to nuget.org remains an operator-controlled step requiring external credentials.

The Scoop extras-bucket manifest is [packaging/scoop/snapture.json](packaging/scoop/snapture.json). It installs the same architecture-specific Velopack portable archives through Scoop, pins their SHA-256 hashes, exposes the `snapture` shim and Start Menu shortcut, and lets Scoop manage upgrades. Copy the manifest into the extras bucket after publishing the matching GitHub Release assets; update its version, URLs, and hashes for each release.

Portable archives include a `Snapture.ini` marker beside `Snapture.App.exe`. Launching that copy automatically keeps settings, history, plugins, logs, autosave, and crash data under `SnaptureData` beside the executable. An unpacked copy can opt in explicitly with `Snapture.App.exe --portable`; the flag also works with every headless CLI command.

**MCP integration:** Settings → Integrations can enable the optional Model Context Protocol server. It exposes loopback-only `http://127.0.0.1:<port>/mcp` tools for monitor/window/region/element/scrolling capture, local OCR, history search, and Auto-redact. The server is off by default, never binds the LAN-share adapter, rotates an in-memory bearer token on every start, and shows that token only while the server is running; MCP clients must send `Authorization: Bearer <token>`. Requests return metadata plus a saved local path unless a tool call explicitly sets `include_image=true`.

### Network and process boundaries

Snapture has no telemetry, analytics, or automatic capture uploads. The maintained source inventory is executable in `OutboundDataFlowAudit`; its test fails if a known HTTP, Kestrel, updater, plugin, or child-process boundary is missing destination, payload, credential, transport, retention, and failure metadata.

- Local AI discovery and inference use only validated loopback endpoints. The selected vision-capable model receives the flattened PNG, prompt, and model ID; no cloud endpoint or API key is accepted. Requests enforce an 8 MB image, 16 MB encoded request, 1 MB response, 8,192-character prompt, 8,192×8,192/50 MP image, and two-minute timeout budget. Oversized, invalid, canceled, unavailable, non-vision, and provider-error responses are classified without sending data elsewhere. Foundry Local discovery may invoke the local `foundry service status` command.
- LAN share and MCP are inbound listeners, disabled unless enabled, and never make outbound requests. LAN share serves only explicitly registered files through expiring single-fetch tokens. MCP requires the in-memory bearer token and returns metadata unless pixels are explicitly requested.
- Check for Updates makes one explicit GitHub request. Installed Velopack builds use the architecture-specific GitHub release feed only after the user confirms download and restart; unpackaged builds open the release page instead.
- Declarative uploaders, Nextcloud, and Immich are explicit actions. Before any upload, Snapture shows the expanded destination, transport (including an HTTP warning), image size/dimensions/source, body type, and credential/header handling. Header and credential values are hidden, and HTTP/transport failures are shown without writing sensitive values to logs.
- Plugin dependency downloads occur only when a plugin explicitly calls `EnsureAsync`, require HTTPS and a SHA-256 pin, and use a per-plugin cache. Plugins are third-party in-process code without an OS sandbox; their declared Network/LaunchProcess capabilities require approval, but their own data handling remains their author's responsibility.
- Explicit external commands, OneOCR, and the magnification fallback are local hidden child processes with shell execution disabled and bounded capture/result paths. Explorer and Windows Settings launches pass only the selected local path or fixed settings URI.

## Usage

After launching, Snapture lives in the system tray.

| Hotkey | Action |
|---|---|
| `PrintScreen` | Capture region |
| `Alt+PrintScreen` | Capture foreground window |
| `Ctrl+PrintScreen` | Capture fullscreen (all monitors) |
| `Shift+PrintScreen` | Recapture last region |
| Tray left-click | Capture region |
| Ctrl+Alt+V | Pin the most recent capture as a Markdown link |
| Tray right-click | Full menu (per-monitor, settings, tools, engine, output folder) |

The Windows 11 taskbar jump list also provides **New region**, **New window**, and **New fullscreen** capture verbs.

All four hotkeys are rebindable from **Settings → Hotkeys**.

**Markdown clipboard integration:** Settings → Output → Clipboard integration can switch automatic clipboard copies from an image to a relative Markdown link. Snapture writes a PNG into the configured vault/export folder and copies a relative image link under the attachment folder. Obsidian vaults are discovered from the active window when possible; Joplin uses the explicitly selected Markdown export or attachment folder because its live resources are managed internally. Ctrl+Alt+V always pins the most recent capture through this flow.

**External command profiles:** Settings → Output → Configure commands supports a full executable path or a command on `PATH`. File mode requires `{file}` and supplies a temporary PNG path; stdin mode pipes PNG bytes directly. From the editor use **External command**, or use Tray → Tools → External commands → **Run on latest capture**. Profiles are user-owned and run only when explicitly selected.

**Declarative uploaders:** Settings → Output → Import uploader accepts the ShareX `.sxcu` schema (or compatible JSON), including self-hosted HTTP endpoints, headers, parameters, multipart file names, and response selectors such as `{json:url}`. From the editor use **Upload**, or use Tray → Tools → Declarative uploaders → **Upload latest capture**. Profiles never run automatically; both actions show a destination/data confirmation before sending.

**Self-hosted destinations:** Settings → Output → Configure Nextcloud / Immich enables either built-in connector. Nextcloud uploads through WebDAV; Immich uploads through `/api/assets` and optionally assigns the returned asset to a configured album. The editor's **Self-hosted** button and Tray → Tools → Self-hosted destinations → **Upload latest capture** show the exact server/path, PNG summary, transport warning, and hidden DPAPI credential handling before the explicit upload.

**Plugin tools:** A plugin can request an `IPluginDependencyStore` from its host and call `EnsureAsync` with a versioned HTTPS URL, simple file name, and SHA-256. Snapture downloads the tool only on that feature request, caches it under the plugin's local data root, and never fetches dependencies during startup or plugin discovery.

**Headless CLI capture:** `Snapture.App.exe --region x,y,width,height --out file.png` captures a fixed rectangle without starting the tray or editor. Add `--fullscreen`, `--engine auto|winrt|gdi`, `--metadata strip|source|replace`, `--icc strip|source|display`, `--provenance off|sidecar`, `--copy`/`--clipboard`, `--profile <name>`, `--lan-share`, `--hold`, `--block <seconds>`, or `--portable` as needed. `--lan-share` keeps its local single-fetch server alive; use `--block` for a bounded hold. `--open <image>` opens an existing image in the editor, while `--convert <image> [--format png|jpg|bmp|webp] [--resize percent] [--out file] [--metadata strip|source|replace] [--icc strip|source] [--provenance off|sidecar]` writes a local converted copy. `--uri` accepts a `snapture://` capture request for protocol activation.

**Per-app capture profiles:** Settings → Output → Per-app capture profiles accepts a Win32 window class name and a built-in preset. The mapping is case-insensitive and applies before region, window, fullscreen, monitor, smart-element, and scrolling captures. Leave the list empty to keep the normal settings unchanged.

**Explorer image verbs:** Tray → Tools → Image shell integration → Install for this user adds an HKCU-only Snapture menu for image files. It can open an image in the editor, convert it to PNG/JPEG, or resize it to a preset percentage. The same actions are available headlessly as `--open <image>`, or `--convert <image> [--format png|jpg|bmp|webp] [--resize percent] [--out file]`. No administrator rights are required; use Remove for this user to undo the registration.

**URL capture handler:** Tray → Tools → URL scheme integration → Install `snapture://` for this user registers local capture links such as `snapture://capture?mode=region&dest=clipboard`. Supported modes are region, window, fullscreen, scrolling, and last-region. The handler rejects UNC/SMB/file URI inputs, traversal, paths outside the user profile, credentials, ports, fragments, and unknown parameters before dispatch; it never opens a supplied file path.

Captures are saved to `%USERPROFILE%\Pictures\Snapture\` by default and copied to the clipboard. The editor window opens after every capture (configurable in `settings.json`).

## Configuration

Installed builds use `%APPDATA%\Snapture\settings.json`. Portable builds use `SnaptureData\settings.json` beside the executable:

```json
{
  "outputFolder": "C:\\Users\\you\\Pictures\\Snapture",
  "filenamePattern": "Snapture_{yyyy-MM-dd}_{HH-mm-ss}",
  "outputFormat": "PNG",
  "copyToClipboard": true,
  "clipboardCopyMode": "image",
  "markdownVaultFolder": "",
  "markdownAttachmentFolder": "attachments",
  "openEditorAfterCapture": true,
  "showToastOnSave": true,
  "launchAtStartup": false,
  "hideDesktopIcons": false,
  "mcpEnabled": false,
  "mcpPort": 9287
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
│  │  ├─ ImageStitcher                 ← Subsampled-SAD seam alignment + sticky-strip detection
│  │  ├─ MonitorEnumerator             ← Per-monitor DPI awareness
│  │  └─ WindowEnumerator              ← Top-level window listing + hit-test
│  ├─ Snapture.Plugin.Abstractions/    ← Public plugin surface (multi-target)
│  │  ├─ PluginAttribute               ← [SnapturePlugin] + capability + host-version bounds
│  │  └─ Contracts                     ← IDestination / ICaptureProcessor / IEditorEffect / IPluginHost
│  └─ Snapture.App/                    ← WPF shell
│     ├─ App.xaml(.cs)                 ← Entry, AUMID, crash logging
│     ├─ Services/
│     │  ├─ AppHost                    ← Lifetime, hotkey wiring, engine swap
│     │  ├─ CaptureOrchestrator        ← Capture → save → clipboard → editor
│     │  ├─ CaptureEngineFactory       ← auto/winrt/gdi resolver
│     │  ├─ HotkeyService              ← RegisterHotKey on a message-only window
│     │  ├─ ClipboardIntegrationService ← Markdown links + vault-safe PNG copies
│     │  ├─ SettingsService            ← JSON load/save
│     │  ├─ PortableMode               ← --portable + Snapture.ini data-root resolver
│     │  ├─ TrayIconHost               ← NotifyIcon + context menu
│     │  ├─ AppIdentity                ← Sets AUMID for borderless-consent persistence
│     │  ├─ BorderlessConsent          ← Win11 22H2+ first-run prompt
│     │  ├─ PrintScreenHijackDetector  ← 24H2 registry probe + reclaim
│     │  ├─ OcrService                 ← Windows AI / Windows OCR / RapidOCR / optional OneOCR wrapper
│     │  ├─ OcrOverlayBuilder           ← OCR line boxes → editable anchored text shapes
│     │  ├─ CaptureHistoryService      ← SQLite + FTS5 + image-feature history index
│     │  ├─ ScrollingCaptureService    ← UIA IScrollProvider driver (alpha)
│     │  ├─ LanShareServer             ← Kestrel + token registry
│     │  ├─ McpServer                  ← Loopback-only Streamable HTTP MCP endpoint
│     │  ├─ PluginLoader               ← AssemblyLoadContext-based plugin host
│     │  ├─ PluginHostBridge           ← IPluginHost implementation
│     │  ├─ StepCaptureSession         ← Click-recorder + Markdown exporter
│     │  └─ GifRecorder                ← Continuous capture loop + animated-GIF encode
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
│        ├─ SettingsWindow             ← Tabbed dialog (General/Capture/Hotkeys/Output/Integrations/Advanced)
│        ├─ ColorPickerWindow          ← HEX / RGB / HSL / APCA-Lc + global click capture
│        ├─ PixelRulerWindow           ← Δx / Δy / length / angle across the virtual screen
│        ├─ SmartCaptureWindow         ← UIA element-level live highlight + click capture
│        ├─ HistoryWindow              ← Thumbnail wall + FTS5 + color/dedup search
│        ├─ OcrResultWindow            ← Recognised-text reviewer
│        ├─ PluginsWindow              ← Plugin inventory + reload
│        ├─ StepCaptureWindow          ← Step Capture review + Markdown/DOCX/PPTX export
│        └─ GifRecordingWindow         ← REC indicator + Stop & save
└─ Resources/Themes/
   ├─ CatppuccinMocha.xaml
   ├─ CatppuccinLatte.xaml
   └─ ThemeStyles.xaml
```

## Privacy

**No telemetry, no analytics, no phone-home, no cloud account.** Every network feature is opt-in and explicitly user-triggered. See [docs/PRIVACY.md](docs/PRIVACY.md) for the audit-grade list of every place Snapture *could* talk to the network, with verification steps.

## License

[MIT](LICENSE)
