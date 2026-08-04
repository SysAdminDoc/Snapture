[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$Clean,
    [switch]$Publish,
    [switch]$Zip,
    [switch]$Msix,
    [switch]$Velopack,
    [ValidateSet('canary', 'pilot', 'stable')]
    [string]$RolloutRing = 'stable',
    [string]$AppInstallerBaseUri = 'https://sysadmindoc.github.io/Snapture'
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

function Get-ProjectVersion {
    $match = Select-String -Path "$root\src\Snapture.App\Snapture.App.csproj" `
        -Pattern '<Version>([^<]+)</Version>'
    if ($null -eq $match) { throw "Could not read the Snapture application version." }
    return $match.Matches[0].Groups[1].Value
}

function New-TileAsset([string]$Path, [int]$Size) {
    try {
        Add-Type -AssemblyName System.Drawing
        $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.Clear([System.Drawing.Color]::FromArgb(203, 166, 247))
            $font = [System.Drawing.Font]::new('Segoe UI', [Math]::Max(10, $Size * 0.52), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
            try {
                $format = [System.Drawing.StringFormat]::new()
                $format.Alignment = [System.Drawing.StringAlignment]::Center
                $format.LineAlignment = [System.Drawing.StringAlignment]::Center
                $graphics.DrawString('S', $font, [System.Drawing.Brushes]::Black, [System.Drawing.RectangleF]::new(0, 0, $Size, $Size), $format)
            }
            finally { $font.Dispose() }
            $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
    catch {
        # makeappx only needs valid PNG resources; keep package generation usable on
        # machines where System.Drawing is not available in the PowerShell host.
        $transparentPng = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII='
        [System.IO.File]::WriteAllBytes($Path, [Convert]::FromBase64String($transparentPng))
    }
}

function New-MsixPackage {
    $version = Get-ProjectVersion
    $packageVersion = if ($version.Split('.').Count -eq 3) { "$version.0" } else { $version }
    $architecture = if ($Runtime -eq 'win-arm64') { 'arm64' } elseif ($Runtime -eq 'win-x64') { 'x64' } else { throw "MSIX supports win-x64 or win-arm64, not '$Runtime'." }
    $msixRoot = Join-Path $root "publish\msix\$Runtime"
    $payload = Join-Path $msixRoot 'payload'
    $staging = Join-Path $msixRoot 'staging'
    $verification = Join-Path $msixRoot 'verification'
    $packageName = "Snapture-v$version-$Runtime.msix"
    $packagePath = Join-Path $root "publish\$packageName"

    foreach ($path in @($payload, $staging, $verification)) {
        if (Test-Path -LiteralPath $path) { [System.IO.Directory]::Delete($path, $true) }
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }
    New-Item -ItemType Directory -Path (Join-Path $staging 'Assets') -Force | Out-Null

    Write-Host "==> dotnet publish -c $Configuration -r $Runtime for MSIX" -ForegroundColor Cyan
    dotnet publish "$root\src\Snapture.App\Snapture.App.csproj" `
        -c $Configuration `
        -r $Runtime `
        --self-contained false `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        -o $payload `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "MSIX publish failed." }

    Copy-Item -Path (Join-Path $payload '*') -Destination $staging -Recurse -Force
    Copy-Item -LiteralPath "$root\build\uninstall.ps1" -Destination (Join-Path $staging 'Uninstall-Snapture.ps1') -Force
    foreach ($asset in @(
        @{ Name = 'Square44x44Logo.png'; Size = 44 },
        @{ Name = 'Square150x150Logo.png'; Size = 150 },
        @{ Name = 'StoreLogo.png'; Size = 50 })) {
        New-TileAsset (Join-Path $staging "Assets\$($asset.Name)") $asset.Size
    }

    $manifestTemplate = Get-Content "$root\packaging\msix\Package.appxmanifest" -Raw
    $manifest = $manifestTemplate.Replace('__PACKAGE_VERSION__', $packageVersion).Replace('__ARCHITECTURE__', $architecture)
    [System.IO.File]::WriteAllText(
        (Join-Path $staging 'AppxManifest.xml'),
        $manifest,
        [System.Text.UTF8Encoding]::new($false))
    if ($manifest -match 'broadFileSystemAccess') { throw 'MSIX manifest must not declare broadFileSystemAccess.' }
    if ($manifest -notmatch 'runFullTrust') { throw 'MSIX manifest must declare runFullTrust.' }
    if ($manifest -notmatch 'windows.startupTask') { throw 'MSIX manifest must declare windows.startupTask.' }

    $makeAppx = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe'
    if (-not (Test-Path -LiteralPath $makeAppx)) {
        $makeAppx = (Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin') -Recurse -Filter makeappx.exe |
            Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } |
            Sort-Object FullName | Select-Object -Last 1).FullName
    }
    if (-not $makeAppx) { throw 'Windows SDK makeappx.exe was not found.' }
    if (Test-Path -LiteralPath $packagePath) { [System.IO.File]::Delete($packagePath) }
    & $makeAppx pack /d $staging /p $packagePath /o
    if ($LASTEXITCODE -ne 0) { throw 'makeappx pack failed.' }
    & $makeAppx unpack /p $packagePath /d $verification /o
    if ($LASTEXITCODE -ne 0) { throw 'makeappx unpack verification failed.' }
    $verifiedManifest = Get-Content (Join-Path $verification 'AppxManifest.xml') -Raw
    if ($verifiedManifest -match 'broadFileSystemAccess') { throw 'Verified MSIX manifest contains broadFileSystemAccess.' }

    $feedPath = if ($RolloutRing -eq 'stable') { '' } else { "/rings/$RolloutRing" }
    $feedUri = "$($AppInstallerBaseUri.TrimEnd('/'))$feedPath/Snapture.appinstaller"
    $packageUri = "$($AppInstallerBaseUri.TrimEnd('/'))$feedPath/$packageName"
    $appInstaller = (Get-Content "$root\packaging\msix\Snapture.appinstaller.template.xml" -Raw).
        Replace('__APPINSTALLER_URI__', $feedUri).
        Replace('__PACKAGE_URI__', $packageUri).
        Replace('__PACKAGE_VERSION__', $packageVersion).
        Replace('__ARCHITECTURE__', $architecture)
    $feedDirectory = Join-Path $msixRoot $RolloutRing
    New-Item -ItemType Directory -Path $feedDirectory -Force | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $feedDirectory 'Snapture.appinstaller'),
        $appInstaller,
        [System.Text.UTF8Encoding]::new($false))

    Write-Host "==> Wrote unsigned MSIX $packagePath" -ForegroundColor Green
    Write-Host "    App Installer feed: $(Join-Path $feedDirectory 'Snapture.appinstaller')" -ForegroundColor Green
    Write-Host "    Rollout ring: $RolloutRing (pinned package version $packageVersion)" -ForegroundColor Green
    Write-Host '    Signing intentionally omitted; release signing remains an operator-controlled step.' -ForegroundColor Yellow
}

function New-VelopackPackage {
    $version = Get-ProjectVersion
    if ($Runtime -notin @('win-x64', 'win-arm64')) { throw "Velopack supports win-x64 or win-arm64, not '$Runtime'." }
    $channel = if ($Runtime -eq 'win-arm64') { 'win-arm64-stable' } else { 'win-x64-stable' }
    $velopackRoot = Join-Path $root "publish\velopack\$Runtime"
    $payload = Join-Path $velopackRoot 'payload'
    if (Test-Path -LiteralPath $velopackRoot) { [System.IO.Directory]::Delete($velopackRoot, $true) }
    New-Item -ItemType Directory -Path $payload -Force | Out-Null

    Write-Host "==> dotnet publish -c $Configuration -r $Runtime for Velopack" -ForegroundColor Cyan
    dotnet publish "$root\src\Snapture.App\Snapture.App.csproj" `
        -c $Configuration `
        -r $Runtime `
        --self-contained false `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        -o $payload `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "Velopack publish failed." }
    Copy-Item -LiteralPath "$root\build\uninstall.ps1" -Destination (Join-Path $payload 'Uninstall-Snapture.ps1') -Force

    Write-Host '==> dotnet tool restore (vpk 1.2.0)' -ForegroundColor Cyan
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Velopack CLI tool restore failed.' }
    dotnet tool run vpk --yes true --legacyConsole true pack `
        --outputDir $velopackRoot `
        --channel $channel `
        --runtime $Runtime `
        --packId SysAdminDoc.Snapture `
        --packVersion $version `
        --packDir $payload `
        --packAuthors SysAdminDoc `
        --packTitle Snapture `
        --releaseNotes "$root\CHANGELOG.md" `
        --mainExe Snapture.App.exe
    if ($LASTEXITCODE -ne 0) { throw 'Velopack pack failed.' }
    $expectedAssets = @(
        "SysAdminDoc.Snapture-$version-$channel-full.nupkg",
        "SysAdminDoc.Snapture-$channel-Portable.zip",
        "SysAdminDoc.Snapture-$channel-Setup.exe",
        "releases.$channel.json",
        "assets.$channel.json",
        "RELEASES-$channel"
    )
    foreach ($asset in $expectedAssets) {
        if (-not (Test-Path -LiteralPath (Join-Path $velopackRoot $asset))) {
            throw "Velopack output is missing '$asset'."
        }
    }
    $feed = Get-Content -LiteralPath (Join-Path $velopackRoot "releases.$channel.json") -Raw | ConvertFrom-Json
    foreach ($asset in $feed.Assets) {
        if (-not (Test-Path -LiteralPath (Join-Path $velopackRoot $asset.FileName))) {
            throw "Velopack feed references missing asset '$($asset.FileName)'."
        }
    }
    Write-Host "==> Wrote Velopack release assets to $velopackRoot" -ForegroundColor Green
    Write-Host '    Signing intentionally omitted; release signing remains an operator-controlled step.' -ForegroundColor Yellow
}

if ($Clean) {
    Write-Host "==> Cleaning bin/obj/publish" -ForegroundColor Cyan
    Get-ChildItem -Path $root -Recurse -Directory -Force `
        | Where-Object { $_.Name -in @('bin','obj','publish') } `
        | ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host "==> dotnet build -c $Configuration" -ForegroundColor Cyan
dotnet build "$root\Snapture.sln" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

if ($Publish) {
    $out = Join-Path $root "publish\$Runtime"
    Write-Host "==> dotnet publish -c $Configuration -r $Runtime --self-contained false" -ForegroundColor Cyan
    dotnet publish "$root\src\Snapture.App\Snapture.App.csproj" `
        -c $Configuration `
        -r $Runtime `
        --self-contained false `
        -p:PublishSingleFile=false `
        -o $out `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

    if ($Zip) {
        $version = (Select-String -Path "$root\src\Snapture.App\Snapture.App.csproj" -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value
        $zip = Join-Path $root "publish\Snapture-v$version-$Runtime.zip"
        if (Test-Path $zip) { Remove-Item $zip -Force }
        Compress-Archive -Path "$out\*" -DestinationPath $zip -Force
        $sha = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
        Set-Content -Path "$zip.sha256" -Value "$sha  $(Split-Path -Leaf $zip)"
        Write-Host "==> Wrote $zip" -ForegroundColor Green
        Write-Host "    SHA256 $sha" -ForegroundColor Green
    }
}

if ($Msix) { New-MsixPackage }
if ($Velopack) { New-VelopackPackage }

Write-Host "==> Done." -ForegroundColor Green
