# Snapture Roadmap

**Version:** 2026-05-08 (post-v0.6 research refresh) · **Tracks:** v0.1.0 → v1.0
**Build philosophy:** WinRT-first · No cloud · No telemetry · Local-first as a feature, not an ideology footnote · Polish that beats Snagit · Knobs that beat ShareX · Modern AI surface (Copilot+ NPU when present) without ever leaving the device.

Items use `[ ]` (open) / `[x]` (shipped). Each line carries a source bracket like `[S3]` or `[S6]` mapping to the Appendix. Tiers: **Shipped** → **Now (v0.7)** → **Next (v0.8)** → **Later (v0.9 / v1.0)** → **Stretch** → **Rejected** (with reasons). Cross-cutting tracks (Security, Accessibility, i18n, Observability, Testing, Docs, Distribution, Migration) live at the bottom and run alongside every release.

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

### v0.7.1 — Video recording (MP4 / HEVC / AV1)

- [ ] **Continuous WGC frames → `Direct3D11CaptureFramePool` queue depth 3** [S3 §1.5]
- [ ] **Media Foundation SinkWriter** path
  - [ ] `MFTEnumEx(MFT_CATEGORY_VIDEO_ENCODER, MFVideoFormat_AV1)` — try AV1 first on D3D12 Video Encode 1.1 capable HW (WDDM 3.2 / Win11 24H2+) [S6 platform]
  - [ ] HEVC fallback (`MFVideoFormat_HEVC`) when AV1 MFT not present
  - [ ] H.264 ultimate fallback (`MFVideoFormat_H264`)
  - [ ] Per-vendor MFT discovery (NVENC / Intel QSV / AMD VCN / Qualcomm MFT on Snapdragon X) [S6 platform]
  - [ ] Software-encode AV1 explicitly **disabled** — battery-killer on laptop class, document the limitation [S6 platform]
- [ ] **`SystemRelativeTime` → PTS** (no QPC conversion needed; both are 100ns) [S3 §7]
- [ ] **`DirtyRegionMode` integration** — skip identical frames; massive battery + file-size win for static-UI screencasts. `IsDirtyRegionSupported` capability check (Win11 24H2+) [S6 platform]
- [ ] **Fragmented MP4 default** — crash-safe partial recovery if Snapture is killed mid-record (wcap pattern) [S6 OSS]
- [ ] **System audio (WASAPI loopback) + mic** with VU meter; both selectable, levels-monitored [self]
- [ ] **App-local audio capture** — record audio from a single window only, exclude system audio (wcap, Cap pattern) [S6 OSS]
- [ ] **Cursor highlight overlay + click animation + click sound** (drawn by Snapture; WGC composites cursor flat) [S3 §1.6, S6 OSS multiple]
- [ ] **Keystroke overlay track** (Snagit + ScreenToGif fragmentation, bundled here) [S2, S6 OSS Cap 0.4.81]
- [ ] **Cursor-telemetry auto-zoom suggestions** — magic-wand button infers zoom-to-clicks from recorded cursor activity (openscreen v1.2 pattern) [S6 OSS]
- [ ] **Auto-tighten / "remove distractions"** — UIA-tree-driven detect-and-crop tabs/dock/taskbar from frames [S6 adjacent]
- [ ] **Pause / resume / trim / segment-split** [S1, S2]
- [ ] **Ring-buffer recording** — last 30/60/90s always available to save (gamer instant-replay style; ScreenToGif #1009) [S6 OSS]
- [ ] **Frame-by-frame editor** — delete / duplicate / set-delay / dither (ScreenToGif's killer feature) [S2 §7, S4]
- [ ] **Aspect-ratio + dimension presets** — 1080p / 4K / 720p / 16:9 / 9:16 / 1:1 quick-pick (ScreenToGif #1447, ShareX bounty #4381) [S6 OSS]
- [ ] **Export bitrate ladder + CRF mode** [S3 §7.3]

### v0.7.2 — GIF + modern formats

- [ ] **GIF path: Magick.NET ≥ 14.12.0** for palette opt + lossy GIF (`-fuzz`); pinned floor avoids CVE-2025-57803 / CVE-2026-33902 / -33901 [S5 §6, S6 sec]
- [ ] **Animated AVIF + APNG output** (eSearch APNG pattern, ScreenToGif #1171, ShareX #6090) [S6 OSS]
- [ ] **Lossless GIF clip-save** (ScreenToGif #1448) [S6 OSS]

### v0.7.3 — HDR + modern still formats

- [ ] **FP16 framepool** when `DisplayInformation.IsHdrSupported` AND output's `ColorSpace == DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020` [S6 platform]
- [ ] **HDR tone-map operator selector** — Reinhard (default) / ACES / Hable [S3 §6.2]
- [ ] **HDR save policy: write all three** — JPEG XL (archival, replaces JXR) + AVIF (sharing) + tone-mapped PNG (legacy SDR consumers). Don't make user choose [S6 platform]
  - Rationale: PDF Association named **JPEG XL** the preferred HDR-image format for PDF (Oct 2025); Microsoft Photos shipped native JXL in 2025.11030.20006.0; Chromium 145 (Feb 2026) ships flagged jxl-rs decode; lossless JPEG transcoding saves 20% size without re-encode [S6 platform]
  - JXR still emitted as opt-in archival format under "Game Bar parity" toggle, but luminance-clamping caveat documented [S6 platform]
- [ ] **HDR Calibration deep-link** — `ms-settings:display` if `MaxLuminance` is suspiciously low; tells user "calibrate before capturing" [S6 platform]
- [ ] **AV1 / HEVC / HEIF detection** — fall back gracefully if Store Codec extension pack is missing [S3 §3.4]
- [ ] **HDR Color Corrector parity** — match Snipping Tool 24H2's toggle in Settings (lack of it is the universal complaint) [S2 §8, S4 HN, S6 community]

### v0.7.4 — Capture quality

- [ ] **`IncludeSecondaryWindows`** toggle in window-mode (popups + menus + dropdowns capture together) [S3 §1.1, S6 platform]
- [ ] **Magnification API fallback** for layered / topmost overlays (Steam overlay, Spotify mini-player) that WGC misses — separate STA helper process to keep WPF main thread clean [S3 §3.3, S6 platform]
- [ ] **Capture monitor under cursor** — distinct hotkey from per-monitor list (Flameshot v14 "Capture Active Monitor") [S6 OSS]
- [ ] **Include cursor in screenshot** option (Flameshot #604 = 109 reactions) [S6 OSS]
- [ ] **Aspect-ratio-locked region presets** (16:9, 9:16, 1:1, 4:3, 4:5) — Shift-drag locks ratio (ShareX bounty #4381) [S6 OSS]
- [ ] **Capture-mode picker modal** — single hotkey opens small wheel/popup to pick region/window/fullscreen/scrolling/text/last-region (Greenshot #1035) [S6 OSS]
- [ ] **Visual-feedback alternative to capture sound** + **optional shutter sound** (Greenshot #1172 a11y, ksnip #962) [S6 OSS]
- [ ] **Browser scrolling capture path** — Chromium 130+ default UIA backend; capture scrolling pages where v0.5 stitcher needs UIA-driven scroll trigger [S3 §5.3]
- [ ] **Lazy-loaded content handling** in scroll capture [S4 capture-full-page.com]
- [ ] **Live preview during scroll** [S1]
- [ ] **Done flow with retake** [S2]

### v0.7.5 — Distribution + Recording polish

- [ ] **ARM64 build matrix** — `dotnet publish -r win-arm64`; ship arm64 ZIP + arm64 winget installer alongside x64. ShareX 20 (Apr 2026) confirmed ARM64 production-ready via MS Store [S6 OSS + community]
- [ ] **Reclaim PrintScreen** UX — surface `ms-settings:easeofaccess-keyboard` deeplink so non-tech users can flip the toggle without regedit (Flameshot #3465 cross-references show most users don't know `PrintScreenKeyForSnippingEnabled` exists) [S6 community]
- [ ] **Bump winget manifest schema** target 1.7.0 → 1.9.0 (or 1.10.0 if GA at release time) [S6 platform]
- [ ] **Right-click colour wheel** on draw to recolour without leaving the canvas (Flameshot UX) [S1]
- [ ] **Sloppiness slider** — hand-drawn aesthetic toggle (Excalidraw-style); psychologically lowers bar-to-share [S4]
- [ ] **Refresh capture preserving annotations** — Snipaste Pro's killer feature; recapture source + re-anchor [S2 §6]
- [ ] **Full transform handles for the Select editor tool** [self / v0.6 deferred]
- [ ] **Pre-warm UIA on app start** if first-attach lag appears in field reports [S3 §5.4]

---

## v0.8 — Editor + AI-local + History wave (NEXT)

The release that closes the editor-polish gap with Snagit / Cap / openscreen and adds the local-AI carve-out.

### v0.8.1 — Editor open-existing + autosave

- [ ] **Drag image into Snapture editor** — drag PNG / JPG / BMP / `.snapture` from anywhere to tray icon or editor canvas → opens for annotation. Flameshot #240 (171 reactions) is the **single most-upvoted feature ask across every Windows OSS screenshot tool** [S6 OSS]
- [ ] **Autosave drafts of in-progress edits** — recover on crash / unexpected close (Flameshot #2055) [S6 OSS]
- [ ] **`.snapture-project` resume** — load + continue editing later (openscreen v1.2 pattern) [S6 OSS]
- [ ] **Tabbed editor** — replace stacked windows for batch edits (Greenshot #1079, three years overdue) [S6 OSS]
- [ ] **Quick Mode** — copy directly to clipboard, skip editor entirely (ksnip #968) [S6 OSS]
- [ ] **Capture Text** hotkey (NormCap pattern) — selection → OCR → clipboard text directly, no image saved. Distinct from "Capture Image then OCR" [S6 OSS]
- [ ] **Annotation duplicate keyboard shortcut** + multiselect (openscreen v1.4) [S6 OSS]
- [ ] **Migrate "Open existing image" decode path off System.Drawing.Common** to SkiaSharp-only — dodges GDI+ CVE-2025-30388 / -47984 / -53766 (CVSS 9.8 RCE on un-patched boxes) [S6 sec]

### v0.8.2 — Background Beautifier + Spotlight + modern shapes

- [ ] **Background Beautifier toggle** — margin / padding / rounded corners / gradient OR image backdrop. ShareX 20.0.2 flagship feature; eSearch + Cap + openscreen all ship variants [S6 OSS]
- [ ] **Spotlight tool** — darken everything outside selection while keeping it sharp (ShareX 19) [S6 OSS]
- [ ] **Auto-border on capture** — outline around the captured image (Greenshot #696, Flameshot #690) [S6 OSS]
- [ ] **Modern arrow style** — Classic / Modern toggle + curved arrows (line tool curve interpolation; ShareX 20.1, Greenshot #311) [S6 OSS]
- [ ] **Bidirectional + reversed-direction arrows** (Flameshot v13) [S6 OSS]
- [ ] **Drop shadows on annotations** (Flameshot #313) [S6 OSS]
- [ ] **Vertical text in editor** (ShareX #4616) [S6 OSS]
- [ ] **Speech-balloon corner-radius slider** (ShareX #7278) [S6 OSS]
- [ ] **Color-picker opacity slider** (Flameshot #249) [S6 OSS]
- [ ] **Saved color palette / swatches** (ShareX #2956) [S6 OSS]
- [ ] **Annotation Categories** (color tags: blocker / question / nit) — Figma pattern [S4]
- [ ] **"Select all of type"** [S2 §1, §6]
- [ ] **Crop with snap-to-edge** — full crop pipeline [self / v0.2 stub]
- [ ] **Eyedropper editor tool** (separate from global picker) [self / v0.2 stub]
- [ ] **Ruler annotation** (drop measurement onto canvas) [self / v0.2 stub]
- [ ] **Hand-drawn aesthetic full implementation** (paired with v0.7 sloppiness slider but wired across every shape) [S4]
- [ ] **Spacebar-toggled side panel** for tool options (Flameshot UX) [S1]
- [ ] **Snappify-style line-state markers** — added (green) / removed (red) / focus / blur / fade per-line on the v0.5 Carbon code-window beautify (turns Snapture into a near-Snappify replacement for free) [S6 adjacent]
- [ ] **Mermaid / PlantUML paste-on-canvas** → render as hand-drawn flowchart (Excalidraw 2024 pattern; differentiator no screenshot tool ships) [S6 adjacent]
- [ ] **SVG vector export** (Skia.Svg writer) [S5 §1]

### v0.8.3 — OCR + extraction + step-capture exports

- [ ] **`TextRecognizer` (Windows AI Foundry, Microsoft.Windows.AI.Imaging)** as primary OCR — NPU-accelerated, per-word confidence, polygonal bounding boxes, model-updatable independent of OS. Fall back to `Windows.Media.Ocr` on non-Copilot+ [S6 platform]
- [ ] **RapidOCR.Net (Apache-2)** as cross-platform fallback. SnapX validated this pick over Tesseract. Pin ONNX Runtime explicitly; gate DirectML behind Settings toggle (DirectML rough as of Mar 2025) [S6 dep]
- [ ] **Sniaste community sp-oneocr pattern** — also expose Win11 24H2+ built-in OneOCR engine for free if available [S6 OSS]
- [ ] **Drag-drop / clipboard-paste / file-select for OCR input** (SnapX) [S6 OSS]
- [ ] **Text overlay anchored to image regions** (SkiaCanvas overlay layer) [S2]
- [ ] **QR-code + barcode extraction** — ZXing.NET pass over capture (Flameshot #511, ShareX, eSearch) [S6 OSS]
- [ ] **OCR table mode** — column reconstruction from word-box geometry [S2]
- [ ] **Step Capture → DOCX / PPTX** export (Snagit 2026 parity; Snagit 2026 added Step→PowerPoint with editable slides + title slide, plus Word) [S6 commercial]
- [ ] **Step Capture → Mermaid `flowchart` block** export (Scribe Mar-2026 BPMN pattern) [S6 adjacent]
- [ ] **Step Capture: keystroke + cursor-click track** alongside screenshots (Cap 0.4.81 timeline tracks) [S6 OSS]

### v0.8.4 — History + library polish

- [ ] **Capture History retention policy** — Unlimited / 30d / 90d / 180d / 1y; once-daily janitor (Raycast pattern) [S6 adjacent]
- [ ] **Dominant-color search column** (k-means via SkiaSharp) + **perceptual-hash dedup column** (~12 lines pHash) — first OSS Windows screenshot tool with color-similarity search of past captures (Eagle parity) [S6 adjacent]
- [ ] **Date / app / tag dropdown filters** [self / v0.3 stub]
- [ ] **"Open in new floating pin"** from History row (Eagle pattern) [S6 adjacent]
- [ ] **"Send to LAN-share"** History row context-menu (paired with v0.4 LAN share) [self / v0.3 stub]
- [ ] **Organize mode** — group screenshots into folders / projects beyond raw chronological list (Capter v4 pattern) [S6 OSS]

### v0.8.5 — Filename + window-context tokens

- [ ] Extend filename pattern with: `{ProcessName}`, `{WindowTitle}`, `{MonitorIndex}`, `{MonitorDpi}`, `{HDR:Y/N}` (PowerToys 0.96 PowerRename added EXIF tokens; same polish bar) [S6 adjacent + self]
- [ ] **Capture presets** — "Bug-report" / "Code-block" / "Documentation" / "Quick-share-LAN" preset templates [S2 §1]
- [ ] **`Microsoft.Windows.Storage.Pickers`** — modernized File/Folder pickers via WinAppSDK 1.8 (works in elevated mode where legacy `Windows.Storage.Pickers` fails) [S6 platform]

### v0.8.6 — Local-AI opt-in (resolves the v0.4 ONNX ambiguity)

- [ ] **Foundry Local + Ollama provider discovery** in Settings — Snapture probes localhost endpoints; if found, surfaces an "AI tools" Settings tab. PowerToys 0.96 Advanced Paste set the precedent [S6 adjacent]
- [ ] **"Send to local LLM"** editor button — sends flattened capture as base64 PNG to chosen local model. Default models: LLaVA via Ollama; Phi-3.5-vision via Foundry Local. Cloud endpoints **explicitly absent from the dropdown** [S6 OSS + adjacent]
- [ ] **`docs/AI-LOCAL.md`** — clarify the local-AI carve-out so the no-cloud anchor is unambiguous; cite the privacy-doc claim chain [self]
- [ ] **Auto-redact Single ONNX path** — RapidOCR's bundled DBNet text-region detector serves both OCR + redact (collapses two model loads into one) [S5 §4]

### v0.8.7 — Clipboard + integration

- [ ] **Clipboard pin** — Ctrl+Alt+V pastes most-recent capture as `![alt](attachments/2026-05-08T12-34-56.png)` AND copies the PNG into the active Obsidian / Joplin vault attachments folder in one shot. Beats the Obsidian web clipper which still doesn't save screenshots natively [S6 adjacent]
- [ ] **Markdown / Obsidian / Joplin clipboard-copy** mode — paste as relative `![alt](relative/path.png)`, optional vault-folder write [S4]
- [ ] **QR-code overlay for LAN-share URLs** — render the share URL as a QR image so a phone on the same Wi-Fi pulls the screenshot with no typing (Excalidraw Oct-2025 QR pattern, applied to v0.4 LAN share) [S6 adjacent]
- [ ] **Hide desktop icons before capture** — `SHELLDLL_DefView` toggle with reliable restore-on-failure [S2]
- [ ] **Hide notifications during capture** — Focus Assist API requires `IUserNotificationListener` + capability declaration [S2]
- [ ] **Multi-pin select + bulk move/close/opacity** [self / v0.2 stub]

---

## v0.9 — Distribution + Plugins + i18n (LATER)

The release that puts Snapture in front of the audience that already uses winget / Chocolatey / Scoop / MS Store, in their language.

### v0.9.1 — Code-signing + signed installers

- [ ] **Code-signing** — primary path via SignPath OSS (free EV cert for OSS); fallback **Azure Artifact Signing** ($9.99/mo, GA April 2026, formerly "Trusted Signing") if SignPath review queue is slow. Velocity > $0 cost for a fast-moving v0.x project [S5 §10, S6 platform]
- [ ] **MSIX package** [S3 §9, S5 §11]
  - [ ] `runFullTrust` capability declared (needed for `RegisterHotKey`)
  - [ ] `windows.startupTask` extension for "Launch at startup"
  - [ ] **No `broadFileSystemAccess`** — use `FileSavePicker` (cleaner, fewer prompts) [S3 §10]
  - [ ] `.appinstaller` URL on GitHub Pages for sideload + auto-update [S3 §9.1]
- [ ] **Auto-update via Velopack** — modern Squirrel successor, MIT, .NET 10 + ARM64 supported, `#:package` directive validated [S5 §10, S6 dep]

### v0.9.2 — Distribution channels

- [ ] **Chocolatey** package (`snapture` + `snapture.portable`, ScreenToGif pattern) [S2]
- [ ] **Scoop manifest** (extras bucket) [S5 §11]
- [ ] **MSI for enterprise** with MST transform — Snagit pattern, sysadmins want SCCM/GPO with silent-install switches [S2 §1]
- [ ] **Microsoft Store ARM64 publish** (alongside ZIP + winget) [S6 OSS]
- [ ] **Submit to `0PandaDEV/awesome-windows`** (under Screen Capture); Snapture not yet listed [S6 OSS]
- [ ] **Context-menu shell integration** — "Open in Snapture editor" right-click for image files; "Resize / Convert" presets (PowerToys Image Resizer pattern) [S4]
- [ ] **CLI mode** — `snapture --region 0,0,800,600 --out file.png --hold --block N --copy --clipboard --lan-share --profile <name>` (Snipaste Pro CLI parity) [S2 §6]
- [ ] **URL-scheme handler** — `snapture://capture?mode=region&autoscroll=true&dest=clipboard` (CleanShot pattern, single-vendor today) [S2 §2]
- [ ] **Per-app capture profiles** — preset auto-applies based on foreground app classname [self / existing roadmap]
- [ ] **`--portable` flag with `Snapture.ini` next to exe** formalized (Flameshot v14 portable binary mode) [S6 OSS]
- [ ] **Win11 Jump List** — taskbar context-menu entries for "New region / window / full" (ScreenToGif #1385) [S6 OSS]
- [ ] **Postpone-before-capture quick toggle** — tray right-click → "Capture in 3s / 5s / 10s" (Greenshot #1063) [S6 OSS]

### v0.9.3 — Plugin SDK polish

- [ ] **Plugin Manager UI from v0.9** — install / update / uninstall / configure UI, not just a folder model. OBS 32.0 lesson: shipping with a folder-only model invites support burn [S6 OSS]
- [ ] **Plugin manifest min/max-host-version** forward-compat guard — refuse to load plugins built for incompatible host (OBS 32.0 pattern) [S6 OSS]
- [ ] **Encrypted-secrets-at-rest for uploader plugins** — required for any plugin declaring `Network` capability that stores credentials. SnapX's post-quantum-resistant secret encryption is the reference [S6 OSS]
- [ ] **`Snapture.Plugin.Abstractions` NuGet** — stable surface for third-party authors (shipped as project ref in v0.4; promote to NuGet here)
- [ ] **`ICaptureSource`** — camera / scanner / file-watch plugin role (in addition to `IDestination` / `ICaptureProcessor` / `IEditorEffect`) [S5 §9]
- [ ] **Capability manifest enforced at install-time** — Snapture surfaces capabilities at install; user reviews [S5 §9]
- [ ] **External Command plugin in-box** — pipe a capture to any CLI binary via stdin / path-arg (Greenshot pattern) [S1]
- [ ] **ShareX-style declarative uploader JSON** — answers Flameshot #499 (29 reactions), parallels CustomUploaders adoption [S1]
- [ ] **`.sxcu`-compatible config import** — let users point Snapture at existing XBackBone / Slink / ShotShare / Myazo endpoint with a JSON config they already own [S4]
- [ ] **NextCloud + Immich destination plugins** in-box, off by default — fits local-first stance, hot OSS request (ScreenToGif #1439, ShareX #8373 = Jan 2026) [S6 OSS]
- [ ] **Auto-bundle external dependencies on demand** — ScreenToGif 2.43 pattern: "tools" plugin pulls ffmpeg / Tesseract on first request rather than ship them in the installer [S6 OSS]

### v0.9.4 — i18n / l10n

- [ ] **Resource-based string extraction** — every UI string in `.resx` (no hardcoded copy) [self]
- [ ] **Initial locales (Phase 1)**: en-US (canonical), de, fr, es, it, pt-BR, nl, pl, cs, ru, tr, ja, zh-Hans, zh-Hant, ko, **ar (RTL)** [S1 + S6 OSS]
- [ ] **OCR language coverage** matches UI locale set (`Windows.Media.Ocr` supports all natively or via Settings deeplink) [S3 §4.2]
- [ ] **Crowdin / Weblate** community translation pipeline
- [ ] **Pluralization-aware strings + RTL-aware layout** (openscreen v1.3, ScreenToGif Arabic installer) [S6 OSS]

### v0.9.5 — Drag-drop + import polish

- [ ] **Drag-and-drop import** — drop image from anywhere onto the tray icon → opens in editor (covers Flameshot #240 = 171 reactions and Greenshot #107) [S1, S6 OSS]
- [ ] **Frozen-screen-before-capture** — capture entire virtual screen instantly, then let user select on the frozen image (CleanShot X pattern; lets user snip menus that would dismiss on click) [S2, S4]
- [ ] **Detect and offer to claim PrintScreen from Snipping Tool first-run** — already shipped in v0.2; promote to first-run wizard (Flameshot v14 polish) [S6 OSS]

---

## v1.0 — Studio + WinUI 3 differentiator extensions (LATER)

Features that earn their place once the core is solid.

- [ ] **Windows App SDK 2.0 XAML Islands hosting** for editor surface — keep WPF shell, drop in WinUI 3 controls (`SystemBackdropElement` for Mica / Acrylic with `CornerRadius`; `ItemsView` for performant virtualized lists; `PopupAnchor` for tooltips). Snapture's WPF shell isolated from full WinUI rewrite [S6 platform]
- [ ] **Smart Crop** on Copilot+ PCs via WinAppSDK 1.7 `ImageScaler` (NPU-accelerated, super-res up to 8×); feature-gated on `IsCopilotPCAvailable` [S6 platform]
- [ ] **Smart Erase / Object Erase** on Copilot+ PCs via WinAppSDK 1.7 Object Erase API; matches Snipping Tool 26H1's "Image Eraser" but ships on every Win11 Copilot+ PC, not just Snapdragon X2-only 26H1 [S6 platform]
- [ ] **Smart Object Selection** on Copilot+ PCs via WinAppSDK 1.7 Image Segmentation API — matches Snipping Tool 26H1's "Object Selector" cross-arch [S6 platform]
- [ ] **Smart Move-equivalent (post-capture object reposition)** — local OpenCvSharp4 ≥ 4.12.0 detect-and-reposition UI rectangles in a flat PNG; Snagit's only single-vendor moat feature [S2 §1, S5 §2, S6 sec]
- [ ] **Magic-wand / heal tool (LaMa or AOT-GAN ONNX)** for object removal — first-run downloader for the model; prefer AOT-GAN if license-strict (Apache-2 code+weights), LaMa if model-quality matters more (CC-BY-NC-SA weights) [S5 §5]
- [ ] **Image Simplifier** — replace text/icons with abstract shapes for wireframe docs (Snagit, single-vendor) [S2 §1]
- [ ] **Multi-track recording** — separate streams (screen / cam / cursor / audio) in one MKV with selectable tracks (Snagit 2026, single-vendor) [S2 §1]
- [ ] **Webcam bubble + layout switching mid-record** (Tella pattern, fragmented elsewhere) [S4]
- [ ] **Webcam shape masks** (circle, rounded rect, custom blob) + **dual-frame preset** (PiP webcam + screen) (Cap, openscreen v1.4) [S6 OSS]
- [ ] **Squircle camera frame + animated background presets** (Tella) [S6 adjacent]
- [ ] **Camera overlay + green-screen keying** via Windows Studio Effects on Copilot+ PCs **— now also works on external/USB cameras** (Win11 build 26100.7309 / 26200.7309, late 2025) [S3 §8.2, S6 platform]
- [ ] **Edit-by-transcript via `Windows.Media.SpeechRecognition`** — local-only, no cloud (Tella's killer feature without the data egress) [S6 adjacent]
- [ ] **Edge-detection ruler** (PowerToys Screen Ruler — measures distance to nearest UI edge, not just dragged pixels) [S4]
- [ ] **Whiteboard-on-desktop** annotation overlay for live screencasts (PicPick) [S2 §4]
- [ ] **Watch folder** — auto-ingest images dropped into a configured folder [self]
- [ ] **Batch-process pipeline** — drop folder, apply effect chain (resize / watermark / border / format), export. Pairs with plugin SDK (PicPick batch) [S2]
- [ ] **Image combiner** (vertical / horizontal / grid stitching, ShareX parity) [S1]
- [ ] **Pinned-overlay multi-canvas** — multiple pins snap-arrange into a comparison board with saved layouts (tldraw pattern, infinite-canvas multi-screenshot collage; PureRef/Eagle reference) [S4, S6 adjacent]
- [ ] **Before/after comparison GIF** — drop two stills, fade-between, export GIF (Shottr-only feature today) [S2 §3]
- [ ] **Code-aware capture full implementation** — detect code blocks (mono-font heuristic + OCR), syntax-highlight, export with v0.5 chrome + drop-shadow + gradient-bg [S4]
- [ ] **Omnidirectional scroll stitching** (horizontal + vertical + diagonal — eSearch pattern) [S6 OSS]
- [ ] **Reverse image search** — opt-in browser launch with selection piped to Google / Bing / Yandex (eSearch) [S6 OSS]

---

## Stretch / nice-to-haves

- [ ] **Linux port** (X11 + Wayland) — would require porting away from WPF; defer until WindowsAppSDK / Avalonia path is decided (SnapX is already this product on .NET 10 + Avalonia — re-evaluate). Far horizon [self, S6 OSS]
- [ ] **macOS port** — same blockers as Linux; the macOS market already has CleanShot X / Shottr + Cap, less compelling [S2]
- [ ] **DX11/12 fullscreen-exclusive game capture via DXGI Output Duplication** in a separate process for SDR + WGC + `IsCursorCaptureEnabled` + `DirtyRegionMode` for HDR. **Don't replicate OBS Game Capture's signed-injection** — that's per-game-cert / per-game-trust territory and anti-cheat heuristics will flag injection [S6 platform]. Process-singleton enforcement: only one Desktop Duplication instance per process [S6 platform]
- [ ] **Browser companion extension** — full-page capture compatible with `chrome.debugger` `Page.captureScreenshot` (sticky-header detection) and an mDNS handoff to Snapture. Cross-process trust boundary keeps it sandboxed
- [ ] **Phone-screen mirror capture** via Phone Link integration **— architecturally blocked.** No public Phone Link Cross-Device frame API as of 2026-05; Microsoft has not signaled one. Workaround: WGC the Phone Link mirror window like any other window (works today, captures a UI layer not source frames) [S3 §8.3, S6 platform]
- [ ] **Snagit `.snag` import** — best-effort, library schema only (file format undocumented; binary-reverse risk too high) [S2]
- [ ] **Encrypted capture folder** — Win11 26H1 native; mirror it for older Windows [S3 §8.3]
- [ ] **MCP server** (Peekaboo pattern) — expose region / window / screenshot capture as MCP tool so Claude Code / Cursor / local agents can request "screenshot the foreground window" or "capture region (x,y,w,h)" programmatically. **No other Windows OSS screenshot tool ships an MCP server today** — strong differentiator [S6 OSS]
- [ ] **`screenshot-to-code` integration** — right-click destination "Send to local LLaVA → HTML/Tailwind" via Ollama only. Cloud variant rejected — local variant a v0.8.6 extension [S6 OSS]
- [ ] **tldraw `make-real`-style sketch-to-HTML** — local-only via Phi-3.5-vision ONNX, opt-in [S6 adjacent]
- [ ] **Screen Translation overlay** — eSearch pattern; replace image text in-place with translation; local model only (NLLB-200 ONNX or similar) [S6 OSS]
- [ ] **Visual feedback option for capture sound (a11y)** — already on roadmap as v0.7; bumped here only if the 2026-05 v0.7 cut runs out of room

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

- [x] **Privacy doc** (`docs/PRIVACY.md`) — explicit "no telemetry, no analytics, no phone-home" statement; lists every network call the app could make + verification steps. Linked from README [S5 §15, S4 Lightshot anti-pattern]
- [x] **`WDA_EXCLUDEFROMCAPTURE` advisory** — capture surfaces a clear toast (not a silent black PNG) [S3 §3.1]
- [ ] **GDI+ CVE mitigation** — migrate the "Open existing image" path off `System.Drawing.Common` decode to SkiaSharp-only, so user-supplied bytes never reach GdiPlus.dll. CVE-2025-30388 (May), CVE-2025-47984 (Jul), **CVE-2025-53766 (Aug, CVSS 9.8 RCE)** all hit GDI+. Patched OS DLL is the OS owner's job; Snapture's job is to not depend on the path [S6 sec]
- [ ] **SQLite CVE mitigation** — bump `SQLitePCLRaw.bundle_e_sqlite3` to a version that bundles SQLite ≥ 3.51.x post **CVE-2025-6965 (CVSS 9.8) and CVE-2025-29088** (~3.50.2 / ~3.49.1 floors) [S6 sec]
- [ ] **Magick.NET ≥ 14.12.0 floor** when introduced (v0.7.2 GIF palette opt) — CVE-2025-57803 (BMP encoder int-overflow, CVSS 9.8), CVE-2026-33902 (FX expression stack overflow), CVE-2026-33901 (MVG decoder heap overflow) all fixed [S6 sec]
- [ ] **OpenCvSharp4 ≥ 4.12.0 floor** when introduced (v1.0 Smart Move) — CVE-2025-53644 (uninitialized-pointer JPEG heap-write) fixed at 4.12.0 [S6 sec]
- [ ] **SkiaSharp 2.88.9 → 3.119.2** maintenance bump — pin to 3.119.2 with explicit `<NoWarn>NU1701</NoWarn>` after validating canvas surface against SkiaSharp issue #3316 (`SkiaSharp.Views.WPF` 3.119.0 restores against `.NETFramework`) [S6 dep]
- [ ] **CommunityToolkit.Mvvm 8.4.0 → 8.4.2** — avoids `<LangVersion>preview</LangVersion>` workaround on .NET 10 (issue #1139, MVVMTK0041) [S6 dep]
- [ ] **Gitleaks-rules-in-repo** with rule-pack version baked into settings so users see exactly which patterns are matched [S5 §4]
- [ ] **Update check** — explicit user click; default off; resolves `https://api.github.com/repos/SysAdminDoc/Snapture/releases/latest` and stores nothing [S5 §15]
- [ ] **Crash dumps** — local `.dmp` only; user opts in to attach to a GitHub issue manually. OBS 32.0 opt-in-only crash-log upload is the explicit pattern reference [S5 §15, S6 OSS]
- [ ] **CVE watch** — Dependabot for `Hardcodet.NotifyIcon.Wpf`, `CommunityToolkit.Mvvm`, `System.Drawing.Common`, `SkiaSharp`, `SkiaSharp.Views.WPF`, `Microsoft.Data.Sqlite`, `SQLitePCLRaw.bundle_e_sqlite3`, `AnimatedGif`, future `Magick.NET`, `Velopack`, `FFMpegCore`, `FlaUI`, `OpenCvSharp4`, `RapidOCR.Net`, `ZXing.Net`
- [ ] **CVE-2023-34634-class deserialization audit** — Greenshot ate one [S1]; explicit policy: no `BinaryFormatter`, no `JavaScriptSerializer`, only `System.Text.Json` with allow-listed converters
- [ ] **Code-signing** via SignPath OSS or Azure Artifact Signing for installer, MSIX, and EXE [S5 §10, S6 platform]
- [ ] **Plugin capability manifest enforced at load time** [S5 §9]
- [x] **LAN share server** — bound to single adapter, private firewall profile only, no public default, single-fetch tokens [S5 §12]
- [ ] **Encrypted-secrets-at-rest for plugin uploaders** — required for any plugin declaring `Network` capability that stores credentials (SnapX reference) [S6 OSS]

### Accessibility

- [ ] **WPF AutomationProperties** on every interactive control — keyboard nav + screen reader compatibility [S2 §1]
- [ ] **Catppuccin Latte (light) + Windows high-contrast follow** [S2]
- [ ] **Keyboard-only capture flow** — every action accessible from keyboard alone; Tab order audited
- [ ] **Visual-feedback alternative to capture sound** (Greenshot #1172) [S6 OSS]
- [ ] **Reduced-motion preference honored** in editor animations (Win11 setting) [S6 platform]
- [ ] **Focus-not-stolen overlay** — capture overlay is non-activating (Raycast / Multi.app pattern); menus the user is screenshotting stay open [S4]
- [x] **APCA contrast readout** in colour picker for designer / a11y workflows [S2 Shottr §3]
- [ ] **`uiAccess=true` signing** — required if Snapture wants to capture elevated targets while running unelevated; PowerToys ColorPicker is the reference [S3 §5.4]

### Internationalization (i18n / l10n)

Phase-1 locales (v0.9.4): en-US (canonical), de, fr, es, it, pt-BR, nl, pl, cs, ru, tr, ja, zh-Hans, zh-Hant, ko, **ar (RTL — every competitor has it)** [S1, S6 OSS]. OCR coverage matches.

### Observability (local-only)

- [ ] **Serilog file + async sinks** at `%LOCALAPPDATA%\Snapture\logs\snapture-.log`, 7-day retention, never network [S5 §15]
- [ ] **Diagnostic dump button** in About dialog — bundles last 7d logs + scrubbed `settings.json` + system info (OS build, GPU, .NET, monitor topology, plugin list, **NPU engagement when smart features ran** — Win11 26H1 Task Manager NPU columns are the reference) + last 20 capture metadata records (no images). Saves to Desktop. PowerToys Bug Report Tool is the reference [S5 §15, S6 platform]
- [ ] **`--verbose` flag** turns on Debug-level logs; default Information-level

### Testing

- [ ] **xUnit** unit tests for `Snapture.Capture` (mocked HMONITOR / HWND, deterministic crop arithmetic)
- [ ] **WPF integration tests** via FlaUI for `Snapture.App` smoke flow [S5 §2]
- [ ] **Visual regression** via Chromatic-style image diff for the editor canvas
- [ ] **Golden-file capture engine** tests — feed a synthetic D3D surface, assert pixel match
- [ ] **CI matrix** — Win10 22H2, Win11 22H2, **Win11 24H2 (26100)**, **Win11 25H2 (26200)**, **Win11 26H1 Experimental (28020)**. Bump on each Insider flight [S6 platform]
- [ ] **Multi-monitor mixed-DPI smoke row** — 100% primary + 125% secondary; the Snipping Tool Jan-2025 KB5050094 "fix" introduced a new regression that's still broken on 26100 [S6 community]

### Documentation

- [ ] **`docs/ARCHITECTURE.md`** — engine seams, settings surface, plugin contracts, threading model
- [x] **`docs/CAPTURE-MATRIX.md`** — engines table + per-Windows-build capability matrix + capture-mode × engine results + WGC limitations + cursor handling + DPI awareness + per-release verification matrix [S3 §1.9]
- [x] **`docs/PRIVACY.md`** — see Security track
- [ ] **`docs/PLUGINS.md`** — interface reference, capability manifest, samples, SnapX-style encrypted-secret-at-rest requirement [S5 §9, S6 OSS]
- [x] **`docs/HOTKEYS.md`** — full keybinding reference
- [ ] **`docs/INSTALL.md`** — explicit ZIP / portable / winget / Chocolatey / Scoop / MSIX instructions; ARM64 paths [S6 community]
- [ ] **`docs/AI-LOCAL.md`** — clarify the local-AI carve-out (Ollama / Foundry Local discovery; cloud endpoints explicitly absent); maintain the no-cloud anchor unambiguously [self]
- [ ] **README screenshots** re-captured on every UI change — system is 125% DPI [self / global rule]
- [ ] **README "Snipping Tool catches 2 things; Snapture catches 30+"** competitive line for Auto-redact [S6 community]
- [ ] **Changelog policy** — every release lists Added/Changed/Fixed/Security; Security entries map to CVE IDs

### Distribution & ops

- [ ] **Branch protection on `main`** with `enforce_admins: true` [self / global rule]
- [x] **Release workflow** in `.github/workflows/release.yml` [self] — extend in v0.7.5 for ARM64 + MSIX + signed artifacts
- [ ] **Dual-arch GitHub Actions matrix** (`win-x64` + `win-arm64`) [S6 OSS]
- [ ] **Microsoft Store ARM64 publish path** (alongside ZIP + winget) [S6 OSS]
- [ ] **README badges** (version / license / platform / .NET / build) kept in sync [self / global rule]
- [ ] **shields.io download counter** on Releases — opt-in distribution telemetry only via user clicking download (no in-app phone-home)

### Migration plan (existing GDI users → WinRT)

1. **v0.2.0 release shipped both engines.** `auto` default picks WinRT on Win10 1809+, GDI on older. Existing settings preserved. ✅
2. **First-run consent prompt** for `GraphicsCaptureAccess.RequestAccessAsync(Borderless)` ✅ [S3 §1.4]
3. **One-shot toast on first WinRT capture** — "Snapture upgraded its capture engine. If anything looks off, switch back in Settings → Capture → Engine."
4. **GDI engine remains shipped through v1.0** as fallback; deprecation only after WinRT proves zero-regression for 6 months.
5. **Settings file forward-compat** — add fields with safe defaults; never break old `settings.json`. ✅
6. **`.snapture` v1 format** locked at v0.2.2 release; v2 (when it comes) writes a `version` field and v0.2 reader rejects unknown versions cleanly.

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
- **Peekaboo** (openclaw/Peekaboo, 3,270★) — macOS CLI + **MCP server for AI agents** to request screenshots. **No Windows OSS equivalent** — Snapture's MCP-server path under Stretch is greenfield [S6 OSS]
- **wcap** (mmozeiko/wcap, 1,194★) — minimalist C; **fragmented MP4 default**, **app-local audio capture**, AAC or FLAC, AV1 + HEVC 10-bit. Fragmented-MP4 pattern adopted in v0.7.1 [S6 OSS]
- **OBS Studio** (72,237★, 32.1.2 2026-04-21) — graphics-hook is the inspiration for game capture; **plugin manager UI** (32.0) is the in-box-installer pattern; **opt-in crash-log upload** is the privacy-respecting telemetry pattern [S6 OSS]
- **screenshot-to-code** (abi/, 72,476★) — drop screenshot → output HTML/Tailwind/React/Vue. Local-LLaVA-via-Ollama pairing under Stretch [S6 OSS]

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

---

## Definition of Done — per release

A release is shippable only when:

- [ ] Build clean across `Snapture.sln` (`dotnet build -c Release`)
- [ ] WinRT engine smoke-test: region + window + fullscreen on Win11 22H2 + 24H2 + **25H2 (26200) + 26H1 Experimental (28020 if available)**; GDI fallback verified on Win10 22H2 [S6 platform]
- [ ] **Multi-monitor mixed-DPI smoke** — 100% primary + 125% secondary; capture across the boundary with both engines [S6 community]
- [ ] Editor smoke-test: every shipped tool drag-tested; undo/redo round-trips; **autosave-recovers-on-restart** path exercised once v0.8.1 lands
- [ ] Settings dialog round-trip: every field reads, edits, saves, reloads
- [ ] All version strings match (`Snapture.App.csproj`, `Snapture.Capture.csproj`, `Snapture.Plugin.Abstractions.csproj`, README badge, CHANGELOG header, About dialog)
- [ ] CHANGELOG entry with Added/Changed/Fixed/Security; Security entries map to CVE IDs
- [ ] CLAUDE.md status line + version-history line updated
- [ ] README screenshots re-captured if UI changed (system is 125% DPI)
- [ ] No telemetry added (grep for `HttpClient` / `WebRequest` / `Socket` / `TcpClient` / `WebSocket` and audit each call site against `docs/PRIVACY.md`)
- [ ] **ARM64 binary published** alongside x64 once v0.7.5 lands [S6 OSS]
- [ ] **CVE floor pin check** for any newly-introduced dependency (Magick.NET ≥ 14.12.0; OpenCvSharp4 ≥ 4.12.0; SQLitePCLRaw bundling 3.51.x or later) [S6 sec]
- [ ] Pushed, tagged `vX.Y.Z`, GitHub Release built via `release.yml`, ZIP+SHA256 round-trip verified
