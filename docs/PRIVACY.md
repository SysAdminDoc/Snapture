# Snapture privacy

**No telemetry. No analytics. No phone-home. No cloud account. Ever.**

Snapture is a desktop tool that runs entirely on your machine. The product is the binary; you are not the product.

This document lists every place Snapture *could* talk to the network, who controls each one, and what data leaves your machine. If you find a path missing from this list, that's a bug — please file an issue.

## What never happens

- Snapture does not register usage, install, launch, or feature events with any analytics provider.
- Snapture does not include AppCenter, Sentry, Firebase, Application Insights, Google Analytics, Plausible, Matomo, or any equivalent.
- Snapture does not upload your captures anywhere by default.
- Snapture does not collect a unique identifier, machine fingerprint, GUID, or "anonymous" telemetry token.
- Snapture does not call home on launch.
- Snapture does not call home on capture.
- Snapture does not call home on save.
- Snapture does not register any background scheduled task that contacts a server.

## What can happen, only if you opt in

The features below contact the network. Each one is off by default. You enable each one explicitly; Snapture remembers your choice in `settings.json` and you can switch any of them off at any time.

### LAN share server (off by default)

Settings → LAN share lets you start a Kestrel HTTP server bound to a single network adapter you choose. When started:

- The server listens on the IP address you picked (never `0.0.0.0`, never the wildcard).
- The server serves only files you've explicitly handed to it via the editor's "Share to LAN" button.
- Each shared file gets a 24-byte URL-safe-base64 token (`/s/{token}`).
- Tokens are single-fetch — the entry is removed from the registry the moment the file is fetched.
- Tokens have a TTL (default 15 minutes); expired entries are removed when next requested.
- The server has no listing endpoint. The root URL responds with a static identification banner only.
- The server never makes outbound requests.
- mDNS announce is **not** included in this version. To share a URL you copy it out of the editor and paste it into your messaging app of choice.

If Windows Firewall prompts you to allow the port, that's the OS asking you to authorise local-network access. If you click "Cancel" the server starts but other machines can't reach it.

### Borderless capture consent (one-time prompt)

On Windows 11 22H2+, the OS draws a yellow border around any window you capture via `Windows.Graphics.Capture` until the app has been granted *borderless capture access*. Snapture asks for this permission on first run via `GraphicsCaptureAccess.RequestAccessAsync(Borderless)`. The result is stored in `settings.json` so you're not asked again.

This call goes only to the Windows runtime. No external network traffic.

### Update check (manual only)

Tray menu → "Check for Updates" resolves `https://api.github.com/repos/SysAdminDoc/Snapture/releases/latest` exactly once per click. There is no automatic background poll, no scheduled task, and no toast notification. The request sends a `User-Agent: Snapture-UpdateCheck/1.0` header and nothing else. The response is parsed in memory and discarded; nothing is stored locally.

### Plugins (third-party code, optional)

If you drop a plugin DLL into `%APPDATA%\Snapture\Plugins\`, that DLL runs in its own collectible `AssemblyLoadContext`. Plugins declare capabilities via `[SnapturePlugin(..., capabilities: PluginCapability.Network | ...)]`. The Plugins window in the tray shows each plugin's declared capabilities so you can see at a glance whether it claims `Network` access.

**Snapture does not sandbox plugins at the OS level.** A plugin you install can talk to the network if it wants to. Treat plugins like any other piece of software you install on your machine: only install code you trust.

## What stays on your machine

- Captures: `%USERPROFILE%\Pictures\Snapture\` by default (configurable in Settings → Output).
- Capture history index: `%LOCALAPPDATA%\Snapture\history\index.db` (SQLite + FTS5 over OCR text + window title + process name).
- Settings: `%APPDATA%\Snapture\settings.json`.
- Crash logs: `%LOCALAPPDATA%\Snapture\crashes\crash_*.txt`.
- Plugin scratch: `%LOCALAPPDATA%\Snapture\plugin-scratch\`.
- Step Capture sessions: `%LOCALAPPDATA%\Snapture\step-sessions\<timestamp>\`.
- LAN share temporary copies: `%LOCALAPPDATA%\Snapture\share\`.

You can delete any of these at any time. Snapture will recreate empty versions on next launch.

## What the OS can see

Some Snapture features ask the OS for information about other windows you have open:

- **Capture history** records the foreground process name and window title at the moment of capture, so you can search "the screenshot I took of Chrome on Tuesday."
- **Step Capture** records every left-click while a session is active to know when to take a screenshot. The hook reads but never modifies clicks.
- **UIA Smart Capture** asks the Windows accessibility API which UIA element is under the cursor.
- **Auto-redact secrets** runs OCR on the captured pixels in-memory.

None of this leaves your machine.

## Compliance posture

### GDPR (EU)

For an EU data subject capturing screens of their own device with their own copy of Snapture: the lawful basis is **consent** (the user installed and runs the software) combined with **legitimate interest** (the user processes their own data on their own device with no controller-processor transfer). Auto-redact, OCR, and the history index process personal data only on the local device; no personal data leaves the device unless the user explicitly shares it via LAN share or a plugin. No supervisory-authority notification is required for purely local processing.

### HIPAA / PHI (US healthcare)

Snapture is not a covered entity and is not a business associate under HIPAA. The auto-redact rule pack includes PHI-sensitive patterns (MRN, NPI, DEA, DICOM UID, date-of-birth markers, patient-name markers) as a redaction aid for users who handle protected health information under their own compliance program. The rule pack does not guarantee HIPAA compliance; it reduces the risk of accidental PHI exposure in screenshots. Hospital IT teams evaluating Snapture should assess it as a local desktop tool that processes images in memory — not as a BAA-bearing cloud service.

### US state privacy law (CCPA / CPRA / VCDPA / CTDPA / UCPA)

Snapture does not collect, sell, or share personal information. There is no triggering event under any US state consumer privacy law because Snapture has no data collection, no analytics, and no advertising surface.

### Plugin compliance

A plugin's compliance posture is the responsibility of the plugin author. Snapture's privacy guarantees cover Snapture's own code. A plugin that declares `Network` capability and transmits data to a remote endpoint is operating outside Snapture's privacy boundary.

## Reporting a privacy issue

If you find a network call this document doesn't list — including a third-party library that phones home unexpectedly — open an issue at https://github.com/SysAdminDoc/Snapture/issues with the tag `privacy`. We treat undocumented network traffic as a bug, not a feature.

## Verifying for yourself

Snapture is open source under the MIT license. To verify any claim in this document:

1. Read the source. The whole codebase is at https://github.com/SysAdminDoc/Snapture.
2. Grep for `HttpClient`, `WebRequest`, `Socket`, `TcpClient`, `WebSocket` — every match should be either inside `LanShareServer.cs` (LAN-bound, opt-in) or `BorderlessConsent.cs` (a system call to the Windows runtime).
3. Run a network sniffer (Wireshark, Process Monitor, Fiddler) while using Snapture. There should be no outbound HTTP / HTTPS / DNS traffic from the Snapture process unless a plugin you installed initiated it.

We mean what we say.
