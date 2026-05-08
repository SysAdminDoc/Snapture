# Snapture Roadmap

**Version:** 2026-05-08 · **Tracks:** Snapture v0.1.0 (shipped) → v1.0
**Build philosophy:** WinRT-first · No cloud · No telemetry · Polish that beats Snagit · Knobs that beat ShareX · Local-first as a feature, not an ideology footnote.

Items use `[ ]` (open) / `[x]` (shipped). Each ROADMAP item carries a source bracket like `[S3]` mapping to the Appendix. Tiers: **Now** (v0.2) → **Next** (v0.3) → **Later** (v0.4–v0.5) → **Differentiators** (v0.6+) → **Stretch** → **Rejected** (with reasons). Cross-cutting tracks (Security, Accessibility, i18n, Observability, Testing, Docs, Distribution, Migration) live at the bottom and run alongside every release.

---

## North star

A Windows screenshot tool the audience that bounced off Snagit's 2025 subscription pivot [S2], Lightshot's 2025 cybersecurity ban [S2], and Snipping Tool's 2024–2025 regression-of-the-month [S2/S4] would actually pick. Five anchors:

1. **WinRT capture parity** with `Windows.Graphics.Capture` (HDR, per-monitor DPI, no flicker, no black Chromium frames) [S3].
2. **Annotation editor** that preserves every shape forever, exports SVG, and ships hand-drawn aesthetics [S5/S4].
3. **Smart Capture** (UIA-driven element selection during snip) — a capability no consumer screenshot tool currently ships [S4].
4. **Auto-redact** secrets locally with model + regex pack — the Lightshot anti-pattern made into a feature [S5/S2].
5. **No telemetry, ever.** Update checks and crash dumps are explicit user actions. State this on the README first line and in the privacy doc [S5/S2].

Non-goals (rejected as misfits — see §Rejected): hosted cloud sharing, account systems, mobile app, AI summarization that round-trips a server, paid tiers, telemetry of any kind.

---

## v0.1.0 — Shipped 2026-05-08

`[x]` Region/Window/Fullscreen capture (GDI), global hotkeys, tray, basic editor (view/save/copy/pin), pin window, JSON settings, Catppuccin Mocha theme, GitHub Actions release workflow [self].

---

## v0.2 — WinRT engine + annotation editor + settings dialog (NOW)

The release that retires GDI as primary and turns the editor from a viewer into an actual annotation tool. Migration sequence in §Migration plan below.

### v0.2.1 — Capture engine: WinRT primary, GDI fallback

- [x] **`WinRtCaptureEngine`** using `Windows.Graphics.Capture` [S3 §1.1]
  - [x] Bump TFM to `net10.0-windows10.0.22621.0` so 22H2 toggles (`IsBorderRequired`, `IncludeSecondaryWindows`, `MinUpdateInterval`) compile [S3 §1.1]
  - [x] D3D11 device interop — hand-rolled (D3D11CreateDevice + CreateDirect3D11DeviceFromDXGIDevice + IDirect3DDxgiInterfaceAccess) instead of Win2D, to keep the dependency footprint at zero [S3 §1.8]
  - [x] `Direct3D11CaptureFramePool.CreateFreeThreaded` (UI-thread variant stutters) [S3 §1.5]
  - [x] BGRA8 path for SDR
  - [ ] FP16 (`R16G16B16A16Float`) path for HDR sources — moved into v0.3.4 with the HDR tonemap work [S3 §1.3]
  - [x] Engine-selector in `SnaptureSettings` (`winrt` / `gdi` / `auto`) [self / existing roadmap]
  - [x] Auto-fallback to GDI on Win10 < 1809 or `GraphicsCaptureItem` creation failure [self]
  - [x] Picker bypass: `CreateForMonitor` for region-select (capture monitor → crop) and `CreateForWindow` for window mode — no UX prompt unless we genuinely need one [S3 §1.6]
- [x] **AppUserModelID** set on app start (`SetCurrentProcessExplicitAppUserModelID("SysAdminDoc.Snapture")`) so border-suppression consent persists across reinstalls [S3 §1.4]
- [x] **First-run consent** for `GraphicsCaptureAccess.RequestAccessAsync(Borderless)` with copy explaining why (the yellow border shows otherwise on Win11 22H2+) [S3 §1.4]
- [ ] **HDR tone-mapping pass** — Win2D `HdrToneMapEffect` (ACES) for SDR PNG export of HDR captures; preserve raw FP16 as JPEG XR for "RAW" archival [S3 §6, S1 ShareX#6688] — moved to v0.3.4
  - This is the single most-requested missing feature across ShareX (108 +1, 222 comments), Greenshot (#542), and Flameshot (#3151) [S1].
- [x] **`WDA_EXCLUDEFROMCAPTURE` aware** — when WGC returns black for a 1Password/Bitwarden/banking window, surface a toast ("This window is excluded by the OS") instead of saving a black PNG [S3 §3.1]
- [ ] **Magnification API fallback** for layered/topmost overlays (Steam overlay, Spotify mini-player) that WGC misses [S3 §3.3] — moved to v0.3 (niche fallback, defer until WGC complaints land)
- [x] **Window-pick mode with hover highlight** — overlay shows the bounds of the window under cursor; PgUp/PgDn walks the parent/child chain (Win32 `GetAncestor`; UIA `TreeWalker` is a v0.4 upgrade once Smart Capture lands) [S4]
- [x] **PrintScreen-on-Win11-24H2** detection — registry check for `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\PrintScreenKeyForSnippingEnabled`; tray surfaces a one-click "Reclaim PrintScreen" entry when the value is set [S5 §8, S1 Flameshot v14 release notes]

### v0.2.2 — Annotation editor (full)

- [x] **SkiaSharp.Views.WPF** as the canvas substrate (GPU-accelerated, 4K/8K-friendly, MIT) [S5 §1]
- [x] **Vector annotation document model** — every shape stays editable forever; flatten only on raster export [S5 §1, S1 Greenshot #375]
- [x] **Tools** (V/R/E/L/A/F/T/H/B/X/N/C hotkeys — `O`/`M`/`I` reserved for v0.2.4 polish):
  - [ ] Select / move / resize / rotate — first-pass: tool exists but only edits-by-creation; full transform handles in v0.3
  - [x] Rectangle (filled / outlined / rounded) — corner-radius via shape model, filled toggle on right panel
  - [x] Ellipse
  - [x] Line (straight / dashed)
  - [x] Arrow (straight / bidirectional / dashed) — curved arrows pushed to v0.3 with the spline editor
  - [x] Freehand pen with mouse-wheel thickness control [S1 Flameshot]
  - [x] Text with editable font/size/weight/colour (basic — bold/italic flags wired in model, dialog plain for now)
  - [x] Highlight (translucent rectangle area-mode) — text-mode + magnify-mode pushed to v0.3
  - [x] Blur / pixelate / **solid-fill redact** as separate tools [S4]
  - [x] Step counter (auto-increment 1, 2, 3) — letter / Roman variants pushed to v0.3
  - [ ] Ruler (drop a measurement onto the canvas) — v0.2.4 polish (lives next to the global pixel-ruler)
  - [ ] Eyedropper (pulls a colour from the underlying capture into the active swatch) — v0.2.4 polish
  - [ ] Crop (with snap-to-edge) — wired to a tool slot, full crop pipeline ships in v0.2.4
- [ ] **Hand-drawn aesthetic toggle** ("sloppiness slider", Excalidraw-style) — psychologically lowers bar-to-share [S4]
- [ ] **Spacebar-toggled side panel** for tool options (Flameshot UX) — right panel is always-on for first pass [S1]
- [ ] **Right-click colour wheel** on draw to recolour without leaving the canvas (Flameshot UX) — v0.3 [S1]
- [x] **Recent-colours bar** (Flameshot pattern) [S1]
- [x] **Open existing image into editor** — File → Open accepts PNG/JPG/BMP and `.snapture` [S1]
- [x] **Undo/redo unlimited** per document — Ctrl+Z / Ctrl+Y, command stack [S1]
- [x] **Border / shadow / rounded-corner / gradient-bg "beautify" wrappers** — checkboxes in right panel apply to both preview and export [S2]
- [x] **Brightness / contrast / grayscale / invert adjustments** (raster pass)
- [x] **Export formats**: PNG, JPG, BMP, **WebP** (Skia native) [S1 ShareX#6090]
  - [ ] AVIF + HDR10 (Win11 AV1 Image Ext) — v0.3.4 with HDR
  - [ ] JPEG XR (HDR archival) — v0.3.4 with HDR
  - [ ] SVG vector export — v0.3 (Skia.Svg writer integration) [S5 §1]
- [x] **`.snapture` project file** (zip = `document.json` + `background.png` + `manifest.json`) [S5 §1]
- [x] **Open `.snapture` again** — round-trip via `SnapFileFormat.Load`
- [ ] **Refresh capture preserving annotations** — Snipaste Pro's killer feature; recapture source + re-anchor — v0.3 [S2 §6]
- [ ] **Annotation Categories** (color tags: blocker / question / nit) — v0.3 (Figma pattern) [S4]
- [ ] **"Select all of type"** — v0.3 (needs the Select tool first) [S2 §1, §6]

### v0.2.3 — Settings dialog (replace JSON-only config)

Tabs: **General · Capture · Hotkeys · Output · Advanced** (Editor + Frame still in-line in the editor itself; we'll split them out once they accumulate enough toggles to deserve a tab)

- [x] CommunityToolkit.Mvvm wired in app csproj; WPF-UI deferred — Catppuccin Mocha already covers theming consistency without an extra dep [S5 §7]
- [x] **Live hotkey recorder** (click field → press combo → bound) — `OnHotkeyPreviewKeyDown` in `SettingsWindow` [self]
- [x] **Per-action hotkey customization** — region / window / fullscreen / last-region all editable
- [ ] **NHotkey wrapper** around `RegisterHotKey` — direct P/Invoke is fine; revisit only if collision UX gets messy [S5 §8]
- [x] Output filename template **variable browser** — `{yyyy-MM-dd}` / `{HH-mm-ss}` etc. shown in the Output tab; `%WindowTitle%` / `%ProcessName%` queued for v0.3 with the capture-context plumbing
- [x] **Engine selector with capability detection** (greys out WinRT on Win10 < 1809 via `EngineCapsText`) [self]
- [x] **Import / export settings.json** (Advanced tab) [S1]
- [ ] **Capture presets** — "Bug-report" / "Code-block" / "Documentation" / "Quick-share-LAN" — v0.3 (needs the LAN-share endpoint from v0.4 and the per-app profile work in v0.5) [S2 §1]

### v0.2.4 — Capture polish (parity table-stakes the existing roadmap missed)

- [x] **Color picker** standalone tool — `ColorPickerWindow` shows HEX / RGB / HSL / APCA-Lc readout (vs white + vs black). Live cursor sample via `Graphics.CopyFromScreen`; click anywhere on screen to lock the colour and copy HEX (low-level mouse hook) [S2 Shottr §3, PowerToys reference S5 §14]
  - [ ] OKLCH readout — v0.3 (need a calibrated OKLCH converter)
  - [ ] Eyedropper as an editor tool (separate from this global picker) — v0.3
- [x] **Pixel ruler** — `PixelRulerWindow` overlays the entire virtual screen, drag to measure Δx / Δy / pixel length / angle [S2, S4]
- [x] **Magnifier loupe** during region-select — `RegionOverlayWindow.UpdateLoupe` shows a 6×-zoomed 20×20-pixel disk with crosshair, pixel coordinate and HEX readout. Auto-flips quadrant near the screen edges [S2]
- [x] **Pin window polish** [S2 §2, S1 ShareX v15.0.0]
  - [x] Opacity slider — context-menu submenu (25/50/75/100%) plus Ctrl-scroll
  - [x] Border on/off + Drop shadow toggle — `B` and `S` keys, also in context menu
  - [x] Click-through mode — Alt+click toggles `WS_EX_TRANSPARENT`
  - [ ] Multi-pin select + bulk move/close/opacity — v0.3
  - [x] **Hot-corner mass hide/show** of all pins — `H` key (works from any focused pin); hot-corner trigger ships in v0.3
  - [x] **Solo mode** — `O` key hides every other pin
- [ ] **Hide desktop icons before capture** toggle — v0.3 (needs SHELLDLL_DefView toggle and reliable restore-on-failure) [S2]
- [ ] **Hide notifications** during capture — v0.3 (Focus Assist API requires `IUserNotificationListener` + capability declaration) [S2]
- [x] **Self-timer / delay capture** 1/3/5/10s (universal) [S2]
- [x] **Last-region recapture** (`Shift+PrintScreen`) — already in old roadmap, keep [self]

---

## v0.3 — Capture parity with the leaders (NEXT)

The release that closes the "real screenshot tool" gap. Validated by being the most-asked feature across all three OSS competitors and Snipaste Pro's paywall [S1, S2].

### v0.3.1 — Scrolling capture

- [x] **UIA `IScrollProvider` driver path** via `System.Windows.Automation` (no FlaUI dep — built-in WPF UIA client is sufficient for v0.3 scope) [S5 §2, S3 §5.2]
  - [ ] Browser path on Chromium 130+ — UIA scroll detection works on the host frame but most pages route scroll through web content; deferred to v0.4 with the image-stitch fallback [S3 §5.3]
  - [x] Native list/scroll path for Office side-panes, Explorer, WPF/WinForms apps that expose `ScrollPattern` directly
  - [ ] Pre-warm UIA on app start — measured first-attach lag in our usage is acceptable; revisit only if user reports it [S3 §5.4]
- [ ] **Image-stitch fallback** — phase-correlation alignment via Math.NET Numerics FFT — v0.4 [S5 §2]
  - [ ] OpenCvSharp4 ORB/AKAZE feature-match for parallax-heavy pages — v0.4 [S5 §2]
  - [ ] **Lazy-loaded content handling** — v0.4 [S4 capture-full-page.com]
- [ ] **Live preview during scroll** — v0.4 [S1]
- [ ] **Done flow with retake** — v0.4 [S2]
- [ ] Sticky-header / sticky-footer detection — v0.4 [S4]

### v0.3.2 — OCR

- [x] **`Windows.Media.Ocr` default** — `OcrService.RecognizeAsync`, used everywhere [S3 §4, S5 §3]
- [x] **Settings deeplink** (`ms-settings:regionlanguage-adddisplaylanguage`) — `OcrService.OpenLanguageInstallSettings()` exposed; called from the empty-result branch of `OcrResultWindow` [S3 §4.4]
- [ ] **Bundled fallback: RapidOCR ONNX (~50 MB)** — v0.4 (needs the model-download flow built first) [S5 §3, S3 §4.3]
- [x] **OCR-region capture** — `CaptureOrchestrator.OcrRegionAsync` selects a region, runs OCR, copies text to clipboard, opens `OcrResultWindow`. Tray Tools → "OCR region…" plus a History row context-menu "Run OCR" entry [self]
- [ ] **Text overlay anchored to image regions** — v0.4 (needs SkiaCanvas overlay layer in the editor) [S2]
- [ ] **QR-code + barcode extraction** — v0.4 (ZXing.Net pass over capture) [S1, S2]
- [ ] **OCR table mode** — v0.4 (Windows.Media.Ocr returns word boxes; column reconstruction is non-trivial) [S2]

### v0.3.3 — GIF / MP4 recording

- [ ] **Continuous WGC frames** to `Direct3D11CaptureFramePool` queue depth 3 [S3 §1.5]
- [ ] **MP4 path: Media Foundation SinkWriter** with H.264 default, HEVC when HW MFT detected, AV1 when D3D12 Video Encode 1.1 available (Win11 24H2+) [S3 §7]
  - [ ] CRF / Quality mode default; CBR available for streamers [S3 §7.3]
  - [ ] Hardware-encode discovery (NVENC / Intel QSV / AMD VCN) [S3 §7.4]
  - [ ] `SystemRelativeTime` → PTS (no QPC conversion needed; both are 100ns) [S3 §7]
- [ ] **GIF path: Magick.NET** for palette optimization + lossy GIF (`-fuzz`) [S5 §6]
- [ ] **Frame-by-frame editor** — delete / duplicate / set-delay / dither (ScreenToGif's killer feature) [S2 §7, S4]
- [ ] **System audio (WASAPI loopback) + mic** — both selectable, levels-monitored [self]
- [ ] **Cursor highlight overlay** — drawn by Snapture (WGC composites cursor flat, can't separate post-hoc) [S3 §1.6]
- [ ] **Click animation + keystroke overlay** (Snagit + ScreenToGif fragmentation, bundle both) [S2]
- [ ] **Cursor smoothing + path editing** in post (Snagit 2026 differentiator) [S2 §1]
- [ ] **Auto-zoom following cursor** during recording (Tella pattern, paywalled in Loom) [S4]
- [ ] **Pause / resume / trim** (ShareX + ScreenToGif parity) [S1, S2]
- [ ] **Export presets**: 8/15/24/30/60 fps; 1080p/4K; bitrate ladder [self]

### v0.3.4 — HDR + modern formats

- [ ] **HDR Color Corrector** toggle (matches Snipping Tool 2025 fix; lack of it is universal complaint) [S2 §8, S4 HN comment]
- [ ] **Tonemap operators**: Reinhard (default), ACES, Hable [S3 §6.2]
- [ ] **WebP / AVIF / JPEG XL output** — answers ShareX #6090 (37 +1) and #5250 (30 +1) [S1]
- [ ] **AV1 / HEVC / HEIF detection** — fall back gracefully if the Store extension pack is missing [S3 §3.4]

### v0.3.5 — Capture extras

- [ ] **Timed capture** — region first, countdown, then refresh-capture (for menus/tooltips/dropdowns) [self / existing roadmap]
- [ ] **Freeze-screen-before-capture** — capture the entire virtual screen instantly, then let the user select on the frozen image (CleanShot X pattern; lets you snip menus that would dismiss on click) [S2, S4]
- [x] **Capture history thumbnail panel** at `%LOCALAPPDATA%\Snapture\history\index.db` with **SQLite + FTS5 over OCR text** — `CaptureHistoryService` + `HistoryWindow` (Tray → Tools → "Capture history…"). First OSS Windows tool with full-text search of capture content [S5 §13, S2 §1, S4 Eagle pattern]
  - [x] Auto-tag by source app (`ProcessName`) and window-title — `CaptureHistoryService.DescribeForeground`
  - [ ] Date / app / tag filters — v0.4 (UI exists but dropdown filters not yet wired; FTS5 search box covers the main path)
  - [x] Right-click → "Re-edit" (Open in editor), "Re-OCR" (Run OCR), "Pin", "Reveal in folder", "Delete from history"
  - [ ] "Send to LAN-share" — v0.4 with the LAN-share server

---

## v0.4 — Differentiators no incumbent ships (LATER)

Items that move ahead of the field, not catch up.

- [ ] **UIA Smart Capture** — hover any window, see live highlights of every UIA element, click-pick exact button/panel for pixel-perfect crop. Snagit's "Smart Move" is post-capture; doing this *during* capture is novel — no consumer tool ships it [S2 §1, S4 a11y-insights pattern]
  - [ ] Hierarchy walking on scroll-wheel (Snipaste Pro's hidden Pro-only feature, generalized) [S2 §6]
  - [ ] Element-aware crop honours bounding rectangles automatically
- [x] **Auto-redact secrets pass** — `Editor/SecretDetector` (Gitleaks-derived rule pack: AWS / GCP / Azure / GitHub / Stripe / Slack / Twilio / JWT / npm / generic hex + PII: credit cards Luhn-validated / SSN / IBAN / IPv4 / MAC / email). `Editor/AutoRedactor` re-runs `Windows.Media.Ocr` over the rendered document, scans each word with the rule pack, drops `RedactShape` solid-fills on matched word-boxes. Editor "Auto-redact secrets" button runs the pipeline and registers each redaction with the command stack so a single undo doesn't strand them [S5 §4, S4 Redacted-extension reference]
  - [x] **Gitleaks rule pack (MIT)** ported as compiled regex
  - [x] **PII recognizers** (credit cards Luhn-validated, SSN, IBAN, IPs, MACs, emails)
  - [ ] **Single ONNX for OCR+redact** — v0.4.x with the RapidOCR bundle [S5 §4]
  - [ ] **Per-rule on/off in settings** — v0.4.x (rules data structure already supports it)
  - [x] **Solid-fill default** for matched secrets (blur is reversible) [S4]
- [ ] **Smart Move-equivalent (post-capture object reposition)** — local OpenCvSharp4 detect-and-reposition UI rectangles in a flat PNG; matches Snagit's only single-vendor moat feature [S2 §1, S5 §2]
- [ ] **LAN-only share server** [S5 §12]
  - [ ] Kestrel minimal API (`<FrameworkReference Include="Microsoft.AspNetCore.App"/>`)
  - [ ] Per-adapter explicit binding — never `0.0.0.0` by default [S5 §12]
  - [ ] mDNS announce via Makaretu.Dns (`_http._tcp.local.`) — opt-in, default off [S5 §12]
  - [ ] Programmatic firewall rule — **private profile only**, refuses public [S5 §12]
  - [ ] One-time tokens (`RandomNumberGenerator.GetBytes(32)`) with TTL + single-fetch eviction [S5 §12]
  - [ ] Optional Tailscale / WireGuard pass-through for off-LAN sharing without uploading
  - [ ] **`.sxcu`-compatible config import** — let users point Snapture at their existing XBackBone / Slink / ShotShare / Myazo endpoint via a JSON config they already own [S4 self-hosted ecosystem]
- [ ] **Code-aware capture / Carbon-style beautify mode** — detect code blocks (mono-font heuristic + OCR), syntax-highlight, export with window-chrome + drop-shadow + gradient-bg framed image. Steals the Carbon / Ray.so / Snappify use case for free [S4]
- [ ] **Pinned-overlay multi-canvas** — multiple pins snap-arrange into a comparison board with saved layouts (tldraw pattern, infinite-canvas multi-screenshot collage) [S4]
- [ ] **Step Capture mode** — auto-record click-through with numbered screenshots, export to **Markdown / DOCX / PowerPoint** as a stepwise guide. Snagit's only consumer-paid feature with no OSS equivalent [S2 §1, S4 Scribe parity]
- [ ] **Before/after comparison GIF** — drop two stills, fade-between, export GIF (Shottr-only feature today) [S2 §3]
- [ ] **Plugin SDK** [S5 §9]
  - [ ] `Snapture.Plugin.Abstractions` NuGet (stable surface for third-party authors)
  - [ ] `IDestination` (clipboard, file, LAN-share, custom HTTP), `IEditorTool`, `IEffect`, `ICaptureSource` (camera, scanner, file watch) [S5 §9]
  - [ ] **AssemblyLoadContext-based, collectible** — plugins hot-reloadable [S5 §9]
  - [ ] **Capability manifest** — plugins declare `requires: ["network", "filesystem.write", "clipboard"]` and Snapture surfaces them at install-time [S5 §9]
  - [ ] **Greenshot destination shape compatibility** — read Greenshot's `IDestination` contract conceptually so existing plugin authors port quickly (no code copy: GPL-1.0) [S1, S5 §9]
  - [ ] **ShareX-style declarative uploader JSON** — answers Flameshot #499 (29 +1) and parallels CustomUploaders adoption [S1]
- [ ] **External Command plugin** in-box — pipe a capture to any CLI binary via stdin/path-arg (Greenshot pattern, lets users wire anything without writing code) [S1]

---

## v0.5 — Distribution polish (LATER)

The release that puts Snapture in front of the audience that already uses winget/Chocolatey/Scoop/MS Store.

- [ ] **MSIX package** — primary install method [S3 §9, S5 §11]
  - [ ] Signed with **SignPath OSS** (free EV cert for OSS projects) [S5 §10]
  - [ ] `.appinstaller` URL on GitHub Pages for sideload + auto-update [S3 §9.1]
  - [ ] `runFullTrust` capability declared (needed for `RegisterHotKey` in Store identity)
  - [ ] `windows.startupTask` extension for "Launch at startup"
  - [ ] **No `broadFileSystemAccess`** — use `FileSavePicker` (cleaner, fewer prompts) [S3 §10]
- [ ] **Portable ZIP** secondary — `--portable` flag switches settings root from `%LOCALAPPDATA%\Snapture\` to `<exedir>\portable\`. Sysinternals/NirSoft doctrine [S4]
- [ ] **winget manifest** at `microsoft/winget-pkgs` (`SysAdminDoc.Snapture.*.yaml`) with both MSIX and portable installers [S3 §9.1, S5 §11]
- [ ] **Chocolatey** package (`snapture` + `snapture.portable`, ScreenToGif pattern) [S2]
- [ ] **Scoop manifest** (extras bucket) [S5 §11]
- [ ] **MSI for enterprise** with MST transform (Snagit pattern — sysadmins want SCCM/GPO deploy with silent-install switches) [S2 §1]
- [ ] **Auto-update via Velopack** — modern Squirrel successor, MIT, supports unsigned + signed, delta updates [S5 §10]
- [ ] **Code-signing**: SignPath OSS for EV signing, immediate SmartScreen reputation [S5 §10]
- [ ] **Context-menu shell integration** — "Open in Snapture editor" on right-click for image files; "Resize / Convert" presets (PowerToys Image Resizer pattern) [S4 PowerToys reference]
- [ ] **Per-app capture profiles** — preset auto-applies based on foreground app classname [self / existing roadmap]
- [ ] **CLI mode** — `snapture --region 0,0,800,600 --out file.png` plus `--hold` (annotate before save), `--block <N>` (sync wait), `--copy`, `--clipboard`, `--lan-share`, `--profile <name>` (Snipaste Pro CLI parity) [S2 §6]
- [ ] **URL-scheme handler** — `snapture://capture?mode=region&autoscroll=true&dest=clipboard` for Raycast / launchers / LeaderKey integration (CleanShot pattern, single-vendor today) [S2 §2]
- [ ] **Drag-and-drop import** — drop an image from anywhere onto the tray icon → opens in editor (covers "open existing image" — Flameshot #240 / Greenshot #107) [S1]

---

## v0.6+ — Differentiator extensions (DIFFERENTIATORS)

Features that earn a place once the core is solid.

- [ ] **Image Simplifier** — replace text/icons with abstract shapes for wireframe docs (Snagit, single-vendor) [S2 §1]
- [ ] **Whiteboard-on-desktop** annotation overlay mode for live screencasts (PicPick) [S2 §4]
- [ ] **Multi-track recording** — separate streams (screen / cam / cursor / audio) in one MKV with selectable tracks (Snagit 2026, single-vendor) [S2 §1]
- [ ] **Webcam bubble** + **layout switching mid-record** (Tella pattern, fragmented elsewhere) [S4]
- [ ] **Edge-detection ruler** (PowerToys Screen Ruler — measures distance to nearest UI edge, not just dragged pixels) [S4]
- [ ] **Magic-wand / heal tool (LaMa or AOT-GAN ONNX)** for object removal — first-run downloader for the model [S5 §5]
  - [ ] Prefer AOT-GAN if license-strict (Apache-2 code+weights); LaMa if model-quality matters more (CC-BY-NC-SA weights)
- [ ] **Camera overlay + green-screen keying** for "me + screen" tutorial captures using Windows Studio Effects on Copilot+ PCs [S3 §8.2]
- [ ] **Markdown / Obsidian / Joplin clipboard-copy** — paste as `![alt](relative/path.png)`, optional vault-folder write [S4]
- [ ] **Watch folder** — auto-ingest images dropped into a configured folder [self / existing roadmap]
- [ ] **Batch-process pipeline** — drop folder, apply effect chain (resize/watermark/border/format), export. Pairs with plugin SDK [S2 PicPick batch]
- [ ] **Image combiner** (vertical / horizontal / grid stitching, ShareX parity) [S1]

---

## Stretch / nice-to-haves

- [ ] **Linux port** (X11 + Wayland) — would require porting away from WPF; defer until WindowsAppSDK / Avalonia path is decided. Far horizon [self]
- [ ] **macOS port** — same blockers as Linux; the macOS market already has CleanShot X/Shottr, less compelling [S2]
- [ ] **DX11/12 fullscreen-exclusive game capture** via injection — d3dshot proves it's possible, ShareX #293 has been open 9 years [S4, S1]. Anti-cheat heuristics will flag injection — gate behind explicit toggle and document the risk
- [ ] **Browser companion extension** — full-page capture compatible with `chrome.debugger` `Page.captureScreenshot` (sticky-header detection) and an mDNS handoff to Snapture. Cross-process trust boundary keeps it sandboxed
- [ ] **Phone-screen mirror capture** via Phone Link integration (Win11 26H1) [S3 §8.3]
- [ ] **Snagit `.snag` import** — best-effort, library schema only (file format is undocumented; binary-reverse risk too high) [S2]
- [ ] **Encrypted capture folder** — Win11 26H1 native; mirror it for older Windows [S3 §8.3]

---

## Rejected (with reasoning)

These items came up in research and are deliberately **not** roadmap candidates. Listed so they don't get silently resurrected.

- **Hosted cloud sharing (Imgur, Gyazo, Streamable, CleanShot Cloud, Loom-style instant link)** — contradicts the "no cloud" anchor. The Lightshot prnt.sc gallery scandal [S2 §10, S4 Kaspersky] is exactly the disaster Snapture is positioned against. **User-controlled LAN/self-hosted endpoints (XBackBone / Slink / ShotShare / Myazo) are NOT cloud and stay on the roadmap** [S4].
- **Account systems / SSO / team workspaces** — not the audience; collaboration belongs in the file-sync layer the user already chose [S2 FuseBase rebrand]
- **Anonymous metrics / telemetry / Sentry / AppCenter** — explicit non-goal. A feature, not an oversight. State in README + privacy doc [S5 §15]
- **AI summarize / cloud transcription / "Ask Copilot about this screenshot"** — Snipping Tool's path; fine for them, not us. Local-LLM ONNX (Phi-3.5-mini) is *not* rejected but deferred indefinitely; it earns its place only when local cost <100ms / capture and value >regex+OCR [S5 §4]
- **Imgur / Dropbox / Box / Flickr / Jira / Confluence destination plugins shipped in-box** — Greenshot's bloat lesson. Plugin SDK supports them, we don't bundle them [S1]
- **Browser-extension-only product (Nimbus/FuseBase pattern)** — not a Windows-tool, not the brief [S2 §9]
- **Subscription pricing / paid Pro tier** — explicit non-goal; the entire pitch is "no subscription" [S2 §1, §6]
- **MS Store as exclusive channel** — restricts MSI/portable distribution, blocks low-level hooks if we ever add them. Ship in Store, but never *only* Store [S3 §9]

---

## Cross-cutting tracks (run alongside every release)

### Security & privacy

- [ ] **Privacy doc** (`docs/PRIVACY.md`) — explicit "no telemetry, no analytics, no phone-home" statement; lists every network call the app could make and gates each behind an explicit user toggle [S5 §15, S4 Lightshot anti-pattern]
- [ ] **`WDA_EXCLUDEFROMCAPTURE` advisory** — when capturing a window the OS marks excluded, surface a clear toast (not a silent black PNG) [S3 §3.1]
- [ ] **Gitleaks-rules-in-repo** with rule-pack version baked into settings so users see exactly which patterns are matched [S5 §4]
- [ ] **Update check** — explicit user click; default off; resolves `https://api.github.com/repos/SysAdminDoc/Snapture/releases/latest` and stores nothing [S5 §15]
- [ ] **Crash dumps** — local `.dmp` only; user opts in to attach to a GitHub issue manually [S5 §15]
- [ ] **CVE watch** — automate Dependabot for `Hardcodet.NotifyIcon.Wpf`, `CommunityToolkit.Mvvm`, `System.Drawing.Common`, future SkiaSharp / Win2D / FFMpegCore / FlaUI / OpenCvSharp4 / Microsoft.Data.Sqlite / Velopack
- [ ] **CVE-2023-34634-class deserialization audit** — Greenshot ate one [S1]; explicit policy: no `BinaryFormatter`, no `JavaScriptSerializer`, only `System.Text.Json` with allow-listed converters
- [ ] **Code-signing** via SignPath OSS for installer, MSIX, and EXE [S5 §10]
- [ ] **Plugin capability manifest** enforced at load time [S5 §9]
- [ ] **LAN share server** — bound to single adapter, private firewall profile only, no public default, single-fetch tokens [S5 §12]

### Accessibility

- [ ] **WPF AutomationProperties** on every interactive control — keyboard nav + screen reader compatibility (the Snipping Tool Slack-share recovery in 2025 was specifically a screen-reader fix) [S2 §1]
- [ ] **High-contrast theme** alongside Catppuccin Mocha (Catppuccin Latte for light, plus Windows high-contrast follow) [S2 PicPick dark-mode reference]
- [ ] **Keyboard-only capture flow** — every action accessible from keyboard alone; Tab order audited
- [ ] **Focus-not-stolen overlay** — capture overlay is non-activating (Raycast / Multi.app pattern); menus the user is screenshotting stay open [S4]
- [ ] **APCA contrast readout** in colour picker for designer/a11y workflows [S2 Shottr §3]
- [ ] **`uiAccess=true` signing** — required if Snapture ever wants to capture elevated targets while running unelevated; PowerToys ColorPicker is the reference [S3 §5.4]

### Internationalization (i18n / l10n)

- [ ] **Resource-based string extraction** — every UI string in `.resx` from v0.2; no hardcoded copy [self]
- [ ] **Initial locales**: en-US (canonical), de, fr, es, it, pt-BR, nl, pl, cs, ru, tr, ja, zh-Hans, zh-Hant, ko (Greenshot's 33-language bar, scoped) [S1]
- [ ] **OCR language coverage** matches UI locale set (Windows.Media.Ocr supports all of the above natively or via Settings deeplink) [S3 §4.2]
- [ ] **Crowdin / Weblate** community translation pipeline for v0.5+

### Observability (local-only)

- [ ] **Serilog file + async sinks** at `%LOCALAPPDATA%\Snapture\logs\snapture-.log`, 7-day retention, never network [S5 §15]
- [ ] **Diagnostic dump button** in About dialog — bundles last 7d logs + scrubbed `settings.json` + system info (OS build, GPU, .NET, monitor topology, plugin list) + last 20 capture metadata records (no images). Saves to Desktop. PowerToys Bug Report Tool is the reference [S5 §15]
- [ ] **`--verbose` flag** turns on Debug-level logs; default Information-level

### Testing

- [ ] **xUnit** unit tests for `Snapture.Capture` (mocked HMONITOR / HWND, deterministic crop arithmetic)
- [ ] **WPF integration tests** via FlaUI for `Snapture.App` smoke flow (open → capture region → verify save → close) [S5 §2]
- [ ] **Visual regression** via Chromatic-style image diff for the editor canvas (paint expected shapes, compare PNG bytes hash) [S4]
- [ ] **Golden-file capture engine** tests — feed a synthetic D3D surface, assert pixel match
- [ ] **CI matrix** — Windows 10 22H2, Windows 11 22H2, Windows 11 24H2, Windows 11 25H2 (Insider) — runners in GH Actions

### Documentation

- [ ] **`docs/ARCHITECTURE.md`** — engine seams, settings surface, plugin contracts, threading model
- [ ] **`docs/CAPTURE-MATRIX.md`** — what works under WGC vs GDI vs Magnification, per-Windows-build [S3 §1.9]
- [ ] **`docs/PRIVACY.md`** — see Security track
- [ ] **`docs/PLUGINS.md`** — interface reference, capability manifest, samples [S5 §9]
- [ ] **`docs/HOTKEYS.md`** — full keybinding reference
- [ ] **README screenshots** re-captured on every UI change — system is 125% DPI [self / global rule]
- [ ] **Changelog policy** — every release lists Added/Changed/Fixed/Security; Security entries map to CVE IDs when applicable [self / global rule]

### Distribution & ops

- [ ] **Branch protection on `main`** with `enforce_admins: true` [self / global rule]
- [ ] **Release workflow** already in `.github/workflows/release.yml` [self] — extend for MSIX + signed artifacts in v0.5
- [ ] **README badges** (version / license / platform / .NET / build) kept in sync with version bumps [self / global rule]
- [ ] **shields.io download counter** on Releases for distribution telemetry users opt into by clicking download — no in-app phone-home

### Migration plan (existing GDI users → WinRT)

1. **v0.2.0 release ships both engines.** `auto` default picks WinRT on Win10 1809+, GDI on older. Existing settings preserved.
2. **First-run consent prompt** for `GraphicsCaptureAccess.RequestAccessAsync(Borderless)` with copy explaining the yellow-border tradeoff [S3 §1.4].
3. **One-shot toast on first WinRT capture** — "Snapture upgraded its capture engine. If anything looks off, switch back in Settings → Capture → Engine."
4. **GDI engine remains shipped through v1.0** as fallback; deprecation only after WinRT proves zero-regression for 6 months.
5. **Settings file forward-compat** — add fields with safe defaults; never break old `settings.json`.
6. **`.snapture` v1 format** locked at v0.2.2 release; v2 (when it comes) writes a `version` field and v0.2 reader rejects unknown versions cleanly.

---

## Competitive watch list

- **Greenshot** — baseline reference. Don't re-add the destination-plugin bloat (Imgur/Dropbox/Box/Flickr/Jira/Confluence). Watch for v1.4 stable cut [S1]
- **ShareX** — destination workflow concept is fine *if* user-configured per endpoint; never bundle 80+. Watch the new Avalonia editor (v20.0.2) [S1]
- **Flameshot** — selection-anchored toolbar UX (rotates with selection) is worth borrowing for the annotation editor. Watch v14.0 stable [S1]
- **CleanShot X** — pinned-overlay polish; All-in-One mode; cloud-self-destruct controversy is our opening [S2 §2]
- **Snagit** — Smart Move + Step Capture are the differentiator-grade features to match; the 2025 subscription pivot lost their community [S2 §1]
- **Shottr** — OCR-overlay anchored to image regions; APCA color picker; before/after GIF [S2 §3]
- **Snipaste** — pin-to-screen polish; refresh-while-preserving-annotations; hierarchy element detection; hot-corner pin toggle [S2 §6]
- **ScreenToGif** — frame-by-frame editor; cache bloat is a cautionary tale (10–15 min recordings filled 50 GB AppData) [S2 §7]
- **Snipping Tool** — Win11's in-box keeps catching up; track 26H1 Insider for AI Smart Object selection, Phone Link capture [S3 §8.3]
- **PowerToys** — Screen Ruler, Color Picker, Image Resizer, Always-On-Top, Text Extractor are the in-box overlap; Snapture's pitch is "everything that's currently 5 PowerToys modules + a real editor in one app" [S4]
- **Cap (Rust/Tauri Loom alt)**, **xland/ScreenCapture (Qt/C++)**, **WinShot (Wails Go+React)**, **Capter (Rust)** — emerging non-.NET OSS Windows alternatives; track for UX patterns [S4]

---

## Appendix — Source bundles

Each `[Sn]` reference in the body maps to one of these dossier bundles. Every concrete claim, feature attribution, complaint, or library recommendation traces here.

### S1 — OSS competitors (Greenshot, ShareX, Flameshot)
- Greenshot — github.com/greenshot/greenshot (4,912 stars 2026-05-08, v1.3.315 stable 2026-03-20, GPL-3.0)
- ShareX — github.com/ShareX/ShareX (37,371 stars, v20.1.0 2026-05-06, GPL-3.0)
- Flameshot — github.com/flameshot-org/flameshot (29,851 stars, v13.3.0 stable 2025-10-28 + v14.0.rc1 2026-04-06, GPL-3.0)
- Greenshot top issues: #115 (HiDPI editor icons, 18 +1), #542 (HDR), #240 (default destination), #525 (.NET 8+), #103 (lettered counter), #311 (curved arrows), #624 (scrolling capture), #203 (video/GIF), #348 (Win11 clipboard refresh), #562 (frozen-stable frustration), #579 (CVE-2023-34634), #375 (.greenshot format)
- ShareX top issues: #6688 (HDR, 108 +1, 222 comments), #6090 (WebP/AVIF/JXL, 37 +1), #5250 (WebP, 30 +1), #6205 (rotate shapes), #5312 (HiDPI multi-monitor), #6653 (Windows on ARM), #848 (DXGI fullscreen games), #3779 (batch image effects), #3992 (multiple upload destinations), #293 (DX11 fullscreen capture, 9 years open), #4381 (aspect-ratio capture)
- Flameshot top issues: #240 (open existing image, 171 +1, top), #1130 (scrolling, 127 +1), #172 (GIF record, 126 +1), #5 (window selection, since 2017), #604 (cursor in screenshot), #702 (OCR), #499 (custom uploaders, 29 +1), #313 (drop shadows), #511 (QR code), #954 (pin tool), #3783 (PrtScn 24H2 hijack)
- ShareX changelog v15-v20, getsharex.com/docs/scrolling-screenshot, github.com/ShareX/CustomUploaders
- Flameshot v14.0 release notes (Tray Menu screen picker, Portable Binary Mode, Screen subcommand redesign)
- flameshot.org/docs/guide/key-bindings/

### S2 — Commercial competitors (Snagit, CleanShot X, Shottr, PicPick, FastStone, Snipaste, ScreenToGif, Snipping Tool, Nimbus/FuseBase, Lightshot)
- Snagit — techsmith.com/snagit (subscription-only since 2025-02-12, $39/yr); features: Step Capture, Smart Move, Image Simplifier, Smart Redact, Background Remover, OCR, Panoramic/Scrolling, Library, Slack/Teams share, MSI+MST enterprise deploy
- CleanShot X — cleanshot.com ($29 hybrid + $8/mo cloud); features: Quick Access Overlay, All-in-One mode, Scrolling, OCR, Floating screenshots, Hide desktop icons, URL-scheme API
- Shottr — shottr.cc ($12 lifetime); features: scrolling, OCR+QR, curved arrows, backdrop, before/after GIF, ruler, APCA color picker, pixel grid
- PicPick — picpick.app ($24 commercial); features: window-control capture, scrolling window, color picker, palette, ruler, magnifier, crosshair, protractor, whiteboard; ships winget
- FastStone Capture — faststone.org ($19.95 lifetime); features: scrolling, multi-window, recorder, focus tool
- Snipaste — snipaste.io ($19.99 Pro); Pro features: Super-snip, hierarchy element detection, OCR, refresh-preserving-annotations, hot-corner toggle, solo mode, custom corner radius, re-edit annotations
- ScreenToGif — github.com/NickeManarin/ScreenToGif (MS-PL); features: frame-by-frame editor, board recorder, key strokes overlay, encoders (System/FFmpeg/Gifski); ships winget+Choco; cache bloat issue #720
- Snipping Tool (Win11) — Text Actions/OCR (23H2), QR (24H2), Image Eraser (24H2), Quick Redact, scrolling capture (25H2), HDR Color Corrector toggle
- Nimbus/FuseBase — thefusebase.com (rebrand 2024 lost users)
- Lightshot — app.prntscr.com (sequential URL enumeration scandal; banned by Missouri S&T 2025-08-05; Kaspersky cryptoscam writeup)
- Vendor pages, support docs, changelogs, Neowin/WindowsCentral/LaptopMag/Thurrott/XDA reviews, Wikipedia entries, AlternativeTo/G2/Capterra reviews

### S3 — Windows capture API dossier
- Windows.Graphics.Capture — learn.microsoft.com/uwp/api/windows.graphics.capture; introduced Win10 1803, window-mode 1809, IsCursorCaptureEnabled 2004, IsBorderRequired/IncludeSecondaryWindows/MinUpdateInterval 22H2, DirtyRegionMode 24H2
- DXGI Desktop Duplication — learn.microsoft.com/windows/win32/direct3ddxgi/desktop-dup-api
- Magnification API — learn.microsoft.com/windows/win32/api/magnification/
- Windows.Media.Ocr — learn.microsoft.com/uwp/api/windows.media.ocr; Settings deeplink ms-settings:regionlanguage-adddisplaylanguage
- UI Automation — IUIAutomationScrollPattern, FlaUI, Chromium 130+ default UIA backend
- HDR — DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709 (scRGB FP16); Win2D HdrToneMapEffect (ACES); JPEG XR for HDR archival
- Media Foundation SinkWriter, D3D12 Video Encode 1.1 (AV1 in Win11 24H2)
- Win11 Snipping Tool feature additions through 25H2; Insider 26H1 (CreateForDisplayId, Phone Link capture)
- winget manifest spec v1.7.0 (May 2025); MSIX vs portable tradeoffs
- microsoft/Windows.UI.Composition-Win32-Samples, microsoft/PowerToys (MeasureTool/ColorPicker), obsproject/obs-studio (graphics-hook), sskodje/ScreenRecorderLib, cube0x8/Capso, microsoft/Windows-classic-samples (DXGIDesktopDuplication, Magnification)

### S4 — Community pain points & adjacent tools
- Reddit-via-blog quotes: scrolling capture missing, HDR washed out, multi-monitor DPI bugs, fullscreen-game black, OCR opaque, Snipping Tool regression complaints
- Microsoft Q&A threads: snipping tool aspect-ratio, multi-monitor 125% scaling, HDR over-exposure, secondary monitor invisible window, "snipping tool slower", "what happened to snipping tool", "doesn't scroll"
- Tech Community: "New Snipping Tool is complete and absolute garbage" thread (4440555)
- HN: news.ycombinator.com/item?id=46815297 (HDR), 40650844 (Flameshot+Tesseract pipe), 26113753, 26446070, 30071766, 26168285
- Eleven Forum: ShareX-vs-Greenshot, is-Lightshot-safe
- Lightshot: Kaspersky cryptoscam, Missouri S&T ban (econnection.mst.edu 2025-08-05), Wired report quoted via peaklinesoftware
- ZxIght, getsharex/docs/scrolling, capture-full-page.com (DevTools full-size lazy-load failure)
- Adjacent tools: Excalidraw (sloppiness slider, ~85k+ stars), tldraw (infinite canvas, ~45k+ stars), Carbon (carbon.now.sh), Ray.so, Snappify, Tella (auto-zoom), Loom, Raycast (action panel), Multi.app activation pattern, OBS (window match priority), Eagle (asset library), ImageGlass, Joplin/Obsidian web clippers, PowerToys Screen Ruler / Image Resizer / Always-On-Top, Sysinternals/NirSoft portable doctrine, Inspect.exe / Accessibility Insights for UIA, Cap (Rust/Tauri), xland/ScreenCapture, WinShot (Wails), Capter (Rust), d3dshot (DX11/12 fullscreen), GoFullPage / FireShot, XBackBone / Slink / ShotShare / Myazo (self-hosted ShareX endpoints)
- Awesome lists: reg-viz/awesome-screenshot, aitaskorchestra/awesome-screenshot-tools, 0PandaDEV/awesome-windows, SonicZhu/Awesome-Windows, deadcoder0904/awesome-website-screenshots, awesome-selfhosted, GitHub topic:screen-capture / topic:screen-annotation
- Roundups: pimpmysnap, ghacks, howtogeek, screensnap.pro, techbloat, zight

### S5 — Technical implementation building blocks
- §1 Vector annotation: SkiaSharp.Views.WPF (MIT, primary), Win2D, custom DrawingVisual; RBush.NET hit-test; SVG via SKSvgCanvas / Svg.NET; tldraw record-store pattern; .snapture zip = document.json + background.png + assets/
- §2 Stitching: Math.NET Numerics FFT (pure-managed, MIT), OpenCvSharp4 (Apache-2, ORB/AKAZE), FlaUI (UIA driver, MIT), avoid Emgu.CV (commercial license)
- §3 OCR: Windows.Media.Ocr default; RapidOCR.NET (~50 MB, MIT-equivalent) bundled fallback; drop Tesseract bundle plan; PaddleOCR overkill
- §4 Secret detection: Gitleaks rules (MIT, ship as-is) + detect-secrets ports; Presidio recognizer ports; RapidOCR's bundled DBNet detector for text-region boxes (one ONNX serves OCR + redact)
- §5 Inpaint: LaMa ONNX (CC-BY-NC-SA weights — caveat); AOT-GAN (Apache-2 code+weights, license-clean alternative)
- §6 GIF/MP4: FFMpegCore (MIT) + LGPL FFmpeg builds (BtbN); Magick.NET for GIF palette opt; Vortice.Windows for Media Foundation SinkWriter; ScreenToGif Editor.xaml as MS-PL reference
- §7 Theming: CommunityToolkit.Mvvm; WPF-UI (lepoco, MIT, recommended over MahApps); Catppuccin Mocha hand-rolled ResourceDictionary
- §8 Hotkey: NHotkey (Apache-2 wrapper around RegisterHotKey); avoid SetWindowsHookEx (AV heuristics, latency); PrintScreen Win11 24H2 registry toggle
- §9 Plugins: AssemblyLoadContext (collectible); IDestination/IEditorTool/IEffect/ICaptureSource; capability manifest; Greenshot shape (read GPL-1.0 contract for design only, no code copy)
- §10 Auto-update: Velopack (MIT, recommended); SignPath OSS (free EV cert for OSS); Certum / Sectigo OV alternatives
- §11 Distribution: winget multi-file YAML schema 1.7.0; Chocolatey + Scoop; MSIX vs portable matrix; EV-cert no longer required for SmartScreen reputation since Apr 2024
- §12 LAN server: Kestrel minimal API (`<FrameworkReference Include="Microsoft.AspNetCore.App"/>`); Makaretu.Dns mDNS; per-adapter binding; INetFwPolicy2 firewall rules (private profile only); RandomNumberGenerator one-time tokens
- §13 DB: Microsoft.Data.Sqlite + FTS5 (recommended); LiteDB for portable mode; sqlite-net-pcl alternative
- §14 Color picker: PowerToys ColorPicker XAML reference; capture 16×16 BGRA region around cursor via WGC instead of GDI GetPixel
- §15 Observability: Serilog file + async sinks; PowerToys Bug Report Tool diagnostic-dump pattern; explicit-update-check via api.github.com/repos/SysAdminDoc/Snapture/releases/latest

---

## Definition of Done — per release

A release is shippable only when:

- [ ] Build clean across `Snapture.sln` (`dotnet build -c Release`)
- [ ] WinRT engine smoke-test: region + window + fullscreen on Win11 22H2 + 24H2; GDI fallback verified on Win10 22H2
- [ ] Editor smoke-test: every shipped tool drag-tested; undo/redo round-trips
- [ ] Settings dialog round-trip: every field reads, edits, saves, reloads
- [ ] All version strings match (`Snapture.App.csproj`, `Snapture.Capture.csproj`, README badge, CHANGELOG header, About dialog)
- [ ] CHANGELOG entry with Added/Changed/Fixed/Security; Security entries map to CVE IDs
- [ ] CLAUDE.md status line + version-history line updated
- [ ] README screenshots re-captured if UI changed (system is 125% DPI)
- [ ] No telemetry added (grep for `HttpClient` / `WebRequest` and audit each call site)
- [ ] Pushed, tagged `vX.Y.Z`, GitHub Release built via `release.yml`, ZIP+SHA256 round-trip verified
