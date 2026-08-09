# Install

## Requirements

- Windows 10 1903+ or Windows 11
- .NET 10 Runtime (download from https://dotnet.microsoft.com/download/dotnet/10.0)

## From GitHub Releases (recommended)

1. Go to [Releases](https://github.com/SysAdminDoc/Snapture/releases).
2. Download `Snapture-vX.Y.Z-win-x64.zip`.
3. Extract to any folder (e.g. `C:\Tools\Snapture\`).
4. Run `Snapture.App.exe`.
5. Snapture appears in the system tray.

No installer needed. To uninstall, delete the folder and optionally clean up:
- `%APPDATA%\Snapture\` (settings)
- `%LOCALAPPDATA%\Snapture\` (history DB, logs, crashes)
- `%USERPROFILE%\Pictures\Snapture\` (saved captures)

## From source

```bash
git clone https://github.com/SysAdminDoc/Snapture.git
cd Snapture
dotnet build -c Release Snapture.sln
dotnet run --project src/Snapture.App -c Release
```

Requires the .NET 10 SDK.

## Via winget (if published to your configured source)

```powershell
winget install SysAdminDoc.Snapture
```

## Portable mode

Run from any folder. Installed copies keep settings in `%APPDATA%\Snapture\settings.json` by default. An unpacked copy can opt into portable mode with `Snapture.App.exe --portable`, or by placing the `Snapture.ini` marker beside the executable. Portable mode stores settings, history, plugins, logs, autosave, and crash data under `SnaptureData` beside the executable.

## ARM64

Release builds include x64 and ARM64 archives. Use the native `win-arm64` archive on ARM64 Windows; the x64 archive remains available for emulation-compatible systems.
