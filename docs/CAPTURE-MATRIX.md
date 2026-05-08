# Snapture capture matrix

Which capture method handles which scenario, broken down per Windows build. Use this when a capture comes out wrong to confirm whether the engine you're on is the right tool for that scenario.

## Engines

Snapture ships two capture engines and an auto-resolver. Switch between them in **Settings → Capture → Capture engine** or via the tray menu's **Capture Engine** submenu.

| Engine | Implementation | OS minimum | Notes |
|---|---|---|---|
| `auto` | `CaptureEngineFactory.Create("auto")` | — | Picks WinRT on Win10 1809+, GDI otherwise |
| `winrt` | `WinRtCaptureEngine` (`Windows.Graphics.Capture` + free-threaded frame pool + D3D11 staging texture) | Windows 10 1809 (build 17763) | Preferred. Honours per-monitor DPI cleanly, captures occluded windows without `PrintWindow` quirks |
| `gdi` | `GdiCaptureEngine` (`BitBlt` + `PrintWindow(PW_RENDERFULLCONTENT)`) | Windows 7+ | Fallback. Blows out HDR highlights, may return black for Chromium / WebView2 |

`Magnification` API path is a v0.7 fallback for Steam-overlay / Spotify-mini-player class scenarios that WGC misses; not implemented in this release.

## Per-Windows-build capability

| OS build | WGC capture | Borderless capture | `IsCursorCaptureEnabled` | `IsBorderRequired` | `DirtyRegionMode` | Snipping Tool PrintScreen hijack | Notes |
|---|---|---|---|---|---|---|---|
| Win10 22H2 (19045) | Yes | n/a | Yes | n/a | n/a | n/a | Stable. Borderless yellow band doesn't apply pre-22H2 |
| Win11 21H2 (22000) | Yes | n/a | Yes | n/a | n/a | n/a | Stable |
| Win11 22H2 (22621) | Yes | **Required for borderless** | Yes | Yes (22H2 toggle) | n/a | n/a | First-run consent prompt persists against AUMID |
| Win11 23H2 (22631) | Yes | Required for borderless | Yes | Yes | n/a | n/a | OCR gains Text Actions |
| Win11 24H2 (26100) | Yes | Required for borderless | Yes | Yes | Yes | **Yes — toggle in Settings → Accessibility → Keyboard** | Snapture detects + offers reclaim from tray |
| Win11 25H2 (Insider) | Yes | Required for borderless | Yes | Yes | Yes | Yes | Snipping Tool gains scrolling capture; doesn't affect us |

`AppUserModelID` is set to `SysAdminDoc.Snapture` first thing in `App.OnStartup`. Without this, the borderless consent prompt re-fires on every reinstall.

## Capture mode × engine results

| Mode | WinRT result | GDI result |
|---|---|---|
| Region (drag-to-select) | Per-monitor DPI correct, no flicker | Works but blends BitBlt across monitors, may shift on per-monitor DPI |
| Foreground window | Captures occluded windows; no black for Chromium | Falls back to screen-rect copy if `PrintWindow` returns black |
| Window picker (manual) | As foreground | As foreground |
| Smart Element Capture | Uses WGC for the chosen UIA element rect | Uses GDI for the chosen UIA element rect |
| Per-monitor | Per-monitor DPI correct | Loses per-monitor DPI on mixed-DPI setups |
| Virtual screen / fullscreen | Per-monitor stitched then scaled to virtual rect | One BitBlt across the virtual rect |
| Scrolling window | Drives UIA `IScrollProvider`, captures via current engine each frame, image-stitcher seam-aligns | Same path; alignment same |

## What WGC will NOT capture

| Scenario | What you see | Workaround in this version |
|---|---|---|
| Window with `WDA_EXCLUDEFROMCAPTURE` (1Password, Bitwarden, banking, DRM video) | All-zero surface | `WinRtCaptureEngine` raises `CaptureExcludedException` → toast instead of black PNG |
| Steam overlay / Spotify mini-player (layered, topmost) | Captured frame may show the underlying app, not the overlay | Magnification API fallback ships in v0.7 |
| Hardware-cursor overlay on Win10 < 1809 | Cursor missing | Switch to GDI (it pulls the cursor from `GetCursorInfo`) |
| Fullscreen-exclusive DX game | Black or torn frame | DXGI Output Duplication path queued for v0.7 stretch |
| HDR display in HDR mode | Highlights clip in BGRA8 path | FP16 + ACES tonemap ships in v0.7 |

## Cursor handling

WGC composites the cursor into the captured frame at the OS level — we cannot toggle it on/off post-hoc. Snapture's per-session "show cursor" toggle therefore only takes effect at capture time:

- WinRT: `GraphicsCaptureSession.IsCursorCaptureEnabled = true/false` (Win10 2004+; OS-guarded by `ApiInformation.IsPropertyPresent` lookup)
- GDI: cursor is never present in BitBlt output; cursor highlight is drawn by the editor on demand (queued for v0.7 with the GIF cursor highlight)

## DPI awareness

`Native.SetProcessDPIAware()` runs first in `AppHost`. WPF then handles per-monitor DPI for windows automatically. The capture engine works in **physical pixels** in **virtual-screen coordinate space**. The virtual screen origin can be negative when secondary monitors are placed left/above primary; `MonitorEnumerator.GetVirtualScreen()` handles this.

If you mount monitors at mixed scales (e.g. 100% primary + 175% secondary), use the WinRT engine — GDI's BitBlt will produce the secondary's logical-pixel content scaled into the virtual-screen physical-pixel rectangle, which never looks right.

## Verification matrix for a release

Before tagging a release, run through:

- [ ] WinRT engine: region + window + fullscreen on Win11 22H2 + 24H2
- [ ] GDI fallback: same on Win10 22H2
- [ ] Borderless consent: first launch on Win11 22H2+, ensure `borderlessConsentGiven=true` lands in settings.json
- [ ] PrintScreen 24H2: enable the OS toggle, verify Snapture's tray surfaces "Reclaim PrintScreen", click it, verify reg value flips to 0
- [ ] WDA_EXCLUDEFROMCAPTURE: capture a 1Password window with WinRT, verify `CaptureExcludedException` toast (not a black PNG)
- [ ] Multi-monitor mixed-DPI region capture: drag across monitors at different scales
- [ ] Scrolling capture: a long Notepad / Settings page (UIA path), verify image-stitcher alignment
- [ ] HDR display (if available): capture a bright source, document that highlights clip until v0.7

This matrix is the source of truth for the per-release verification step in the Definition of Done.
