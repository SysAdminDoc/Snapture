# Snapture hotkey reference

This document is the canonical list. All hotkeys outside the editor are rebindable from **Settings → Hotkeys**; the editor hotkeys are tool shortcuts and are not user-configurable in this version.

## Global (system-wide)

These work regardless of which app has focus. They're registered via `RegisterHotKey` on a message-only window so they survive even when no Snapture window is visible.

| Default | Action |
|---|---|
| `PrintScreen` | Capture region (drag-to-select with frozen-screen overlay + magnifier loupe) |
| `Alt + PrintScreen` | Capture foreground window |
| `Ctrl + PrintScreen` | Capture all monitors |
| `Shift + PrintScreen` | Recapture last region (requires having captured one this session or last) |

If Windows 11 24H2's "PrintScreen opens Snipping Tool" toggle is on, the tray will surface a one-click **Reclaim PrintScreen** entry. See [PRIVACY.md](PRIVACY.md) for what that registry change does.

Tray menu mirrors every hotkey-bound action plus extras:

- Capture Region · Recapture Last Region · Pick Window · Smart Element Capture · Capture Foreground Window · Capture Fullscreen · Capture Scrolling Window (alpha) · Capture Monitor (per-monitor submenu) · Capture with Delay (1/3/5/10s submenu)
- Settings · Tools (Color picker / Pixel ruler / OCR region / Record GIF / Step Capture / Plugins / Capture history) · Open Output Folder · Capture Engine (auto/winrt/gdi) · About · Quit

## Editor

The annotation editor is a standalone window. Tool selection is single-letter; commands use `Ctrl` modifier.

### Tool selection

| Key | Tool |
|---|---|
| `V` | Select / move (first-pass — full transform handles in v0.7) |
| `R` | Rectangle |
| `E` | Ellipse |
| `L` | Line |
| `A` | Arrow |
| `F` | Freehand pen |
| `T` | Text |
| `H` | Highlight (translucent rectangle) |
| `B` | Blur / Pixelate |
| `X` | Solid-fill Redact |
| `N` | Step counter (auto-increments) |
| `C` | Crop (slot reserved; full crop pipeline ships in v0.7) |

### Commands

| Key | Action |
|---|---|
| `Ctrl + Z` | Undo |
| `Ctrl + Y` | Redo |
| `Ctrl + S` | Save `.snapture` project |
| `Ctrl + E` | Export PNG (or last format used in Export As…) |
| `Ctrl + O` | Open existing image or `.snapture` |
| `Ctrl + C` | Copy flattened result to clipboard |
| `Delete` | Remove last shape (no selection model in this version) |
| `Mouse wheel` | Adjust active stroke thickness |
| `Right-click` | (no action — context menu deferred) |

## Pin window

A pinned image is a borderless, always-on-top window. Drag with the mouse to move; right-click to close.

| Key | Action |
|---|---|
| `Esc` | Close pin |
| `B` | Toggle border |
| `S` | Toggle drop shadow |
| `H` | Hide / show **all** pins (hot-corner toggle queued for v0.7) |
| `O` | Solo this pin (hide every other pin) |
| `Mouse wheel` | Zoom |
| `Ctrl + Mouse wheel` | Adjust opacity |
| `Alt + click` | Toggle click-through (`WS_EX_TRANSPARENT`) |

The pin window's context menu also exposes Copy, Reset 100%, Opacity submenu (25/50/75/100%), and the toggles above.

## Region overlay

Active during region selection. The overlay shows a frozen virtual-screen image dimmed behind a selection rectangle, plus a 6× magnifier loupe near the cursor.

| Key | Action |
|---|---|
| `Esc` | Cancel |
| `Enter` | Confirm current selection |
| Drag | Define selection rectangle |

## Window picker

Active when the user picks **Pick Window…** from the tray.

| Key | Action |
|---|---|
| `Esc` or right-click | Cancel |
| `Enter` | Capture currently highlighted window |
| `PgUp` / `PgDn` | Walk parent / child of current hover (Win32 ancestor chain) |
| Click | Capture window under cursor |

## Smart Element Capture

Active when the user picks **Smart Element Capture…** from the tray. Highlights individual UIA elements live.

| Key | Action |
|---|---|
| `Esc` or right-click | Cancel |
| `Enter` | Capture currently locked element |
| `PgUp` | Climb to parent in `TreeWalker.RawViewWalker` (locks the manual stack) |
| `PgDn` | Release lock — go back to live cursor tracking |
| Click | Capture currently hovered/locked element |

## Color picker

The dedicated color-picker window installs a low-level mouse hook for the duration of its lifetime so a click anywhere on screen locks the colour and copies HEX to the clipboard.

| Key | Action |
|---|---|
| `Esc` | Cancel |
| Click anywhere | Copy HEX of the pixel under cursor and dismiss |
| **Copy HEX** button | Copy the currently-displayed HEX |

## Pixel ruler

| Key | Action |
|---|---|
| `Esc` | Cancel |
| Drag | Define the measurement (Δx / Δy / pixel length / angle update live) |

## Step Capture

Click-recording is opt-in via the Step Capture window's **Start recording** button. The session window is non-blocking; you click your way through the workflow, frames stack up in the review list, and **Stop recording** ends the hook. Markdown export is via the **Export Markdown** button.

There are no global hotkeys for Step Capture in this version — start/stop is intentionally explicit so a stray PrintScreen doesn't terminate a running session.
