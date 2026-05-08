# Changelog

All notable changes to Snapture will be documented in this file.

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
