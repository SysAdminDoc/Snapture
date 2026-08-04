[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$Clean,
    [switch]$Publish,
    [switch]$Zip,
    [switch]$Msix,
    [switch]$Msi,
    [switch]$Velopack,
    [switch]$Chocolatey,
    [switch]$NuGet,
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

function Invoke-Wix {
    param([string[]]$Arguments)

    & dotnet tool run wix -- @Arguments
    if ($LASTEXITCODE -ne 0) { throw "WiX command failed: wix $($Arguments -join ' ')" }
}

function Get-MsiProductCode([string]$Version) {
    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes("SysAdminDoc.Snapture.MSI.$Version")
        $hash = $md5.ComputeHash($bytes)
        return ([Guid]::new($hash)).ToString('B').ToUpperInvariant()
    }
    finally { $md5.Dispose() }
}

function New-MsiPackage {
    $version = Get-ProjectVersion
    if ($Runtime -notin @('win-x64', 'win-arm64')) { throw "MSI supports win-x64 or win-arm64, not '$Runtime'." }
    $architecture = if ($Runtime -eq 'win-arm64') { 'arm64' } else { 'x64' }
    $msiRoot = Join-Path $root "publish\msi\$Runtime"
    $payload = Join-Path $msiRoot 'payload'
    $verification = Join-Path $msiRoot 'verification'
    $baseMsi = Join-Path $msiRoot "Snapture-v$version-$Runtime.msi"
    $enterpriseMsi = Join-Path $msiRoot "Snapture-v$version-$Runtime-enterprise-source.msi"
    $transform = Join-Path $msiRoot "Snapture-v$version-$Runtime-enterprise.mst"
    $source = Join-Path $root 'packaging\msi\Snapture.wxs'

    if (Test-Path -LiteralPath $msiRoot) { [System.IO.Directory]::Delete($msiRoot, $true) }
    New-Item -ItemType Directory -Path $payload, $verification -Force | Out-Null

    Write-Host "==> dotnet publish -c $Configuration -r $Runtime for MSI" -ForegroundColor Cyan
    dotnet publish "$root\src\Snapture.App\Snapture.App.csproj" `
        -c $Configuration `
        -r $Runtime `
        --self-contained false `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        -o $payload `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "MSI publish failed." }
    Copy-Item -LiteralPath "$root\build\uninstall.ps1" -Destination (Join-Path $payload 'Uninstall-Snapture.ps1') -Force
    if (-not (Test-Path -LiteralPath (Join-Path $payload 'Snapture.App.exe'))) {
        throw 'MSI payload is missing Snapture.App.exe.'
    }

    Write-Host '==> dotnet tool restore (WiX 5.0.2)' -ForegroundColor Cyan
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'WiX tool restore failed.' }
    $productCode = Get-MsiProductCode $version
    $commonArgs = @(
        'build',
        '-arch', $architecture,
        '-d', "PackageVersion=$version",
        '-d', "ProductCode=$productCode",
        '-d', "PayloadDir=$payload"
    )

    Invoke-Wix ($commonArgs + @('-d', 'StartMenuName=Snapture', '-pdbtype', 'none', $source, '-out', $baseMsi))
    Invoke-Wix ($commonArgs + @('-d', 'StartMenuName=Snapture Enterprise', '-pdbtype', 'none', $source, '-out', $enterpriseMsi))
    Invoke-Wix @('msi', 'validate', $baseMsi)
    Invoke-Wix @('msi', 'transform', $baseMsi, $enterpriseMsi, '-p', '-out', $transform)

    foreach ($artifact in @($baseMsi, $transform)) {
        if (-not (Test-Path -LiteralPath $artifact)) { throw "MSI build is missing '$artifact'." }
        if ((Get-Item -LiteralPath $artifact).Length -le 0) { throw "MSI build produced an empty '$artifact'." }
    }

    $decompiledBase = Join-Path $verification 'base.wxs'
    $decompiledEnterprise = Join-Path $verification 'enterprise.wxs'
    $transformOutput = Join-Path $verification 'enterprise-transform.wixout'
    $transformSource = Join-Path $verification 'enterprise-transform'
    Invoke-Wix @('msi', 'decompile', '-o', $decompiledBase, $baseMsi)
    Invoke-Wix @('msi', 'decompile', '-o', $decompiledEnterprise, $enterpriseMsi)
    $baseSource = Get-Content -LiteralPath $decompiledBase -Raw
    $enterpriseSource = Get-Content -LiteralPath $decompiledEnterprise -Raw
    if ($baseSource -notmatch 'Name="Snapture"') { throw 'Base MSI shortcut name was not found during verification.' }
    if ($enterpriseSource -notmatch 'Name="Snapture Enterprise"') { throw 'Enterprise MSI shortcut variant was not found during verification.' }
    if ($baseSource -match 'Name="Snapture Enterprise"') { throw 'Base MSI unexpectedly contains the enterprise shortcut name.' }

    Invoke-Wix @('msi', 'transform', $baseMsi, $enterpriseMsi, '-p', '-xo', '-out', $transformOutput)
    Expand-Archive -LiteralPath $transformOutput -DestinationPath $transformSource -Force
    $transformSourceXml = Get-Content -LiteralPath (Join-Path $transformSource 'wix-wid.xml') -Raw
    if ($transformSourceXml -notmatch '<table name="Shortcut"') { throw 'The enterprise MST contains no Shortcut table changes.' }
    if ($transformSourceXml -notmatch 'Snapture Enterprise') { throw 'The enterprise MST does not contain the expected shortcut name change.' }

    [System.IO.File]::Delete($enterpriseMsi)
    Write-Host "==> Wrote unsigned MSI $baseMsi" -ForegroundColor Green
    Write-Host "    Wrote enterprise transform $transform" -ForegroundColor Green
    Write-Host '    Silent install: msiexec /i Snapture.msi TRANSFORMS=Snapture-enterprise.mst /qn /norestart' -ForegroundColor Green
    Write-Host '    Signing intentionally omitted; release signing remains an operator-controlled step.' -ForegroundColor Yellow
}

function New-VelopackPackage {
    param(
        [string]$PackageRuntime = $Runtime
    )

    $version = Get-ProjectVersion
    if ($PackageRuntime -notin @('win-x64', 'win-arm64')) { throw "Velopack supports win-x64 or win-arm64, not '$PackageRuntime'." }
    $channel = if ($PackageRuntime -eq 'win-arm64') { 'win-arm64-stable' } else { 'win-x64-stable' }
    $velopackRoot = Join-Path $root "publish\velopack\$PackageRuntime"
    $payload = Join-Path $velopackRoot 'payload'
    if (Test-Path -LiteralPath $velopackRoot) { [System.IO.Directory]::Delete($velopackRoot, $true) }
    New-Item -ItemType Directory -Path $payload -Force | Out-Null

    Write-Host "==> dotnet publish -c $Configuration -r $PackageRuntime for Velopack" -ForegroundColor Cyan
    dotnet publish "$root\src\Snapture.App\Snapture.App.csproj" `
        -c $Configuration `
        -r $PackageRuntime `
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
        --runtime $PackageRuntime `
        --packId SysAdminDoc.Snapture `
        --packVersion $version `
        --packDir $payload `
        --packAuthors SysAdminDoc `
        --packTitle Snapture `
        --releaseNotes "$root\CHANGELOG.md" `
        --mainExe Snapture.App.exe
    if ($LASTEXITCODE -ne 0) { throw 'Velopack pack failed.' }
    Add-PortableMarkerToArchive (Join-Path $velopackRoot "SysAdminDoc.Snapture-$channel-Portable.zip")
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

function Add-PortableMarkerToArchive([string]$ArchivePath) {
    if (-not (Test-Path -LiteralPath $ArchivePath)) { throw "Portable archive '$ArchivePath' was not found." }
    $stage = Join-Path (Split-Path -Parent $ArchivePath) ([System.IO.Path]::GetRandomFileName())
    $rewritten = "$ArchivePath.rewritten.zip"
    try {
        New-Item -ItemType Directory -Path $stage -Force | Out-Null
        Expand-Archive -LiteralPath $ArchivePath -DestinationPath $stage -Force
        Copy-Item -LiteralPath (Join-Path $root 'packaging\portable\Snapture.ini') `
            -Destination (Join-Path $stage 'Snapture.ini') -Force
        if (Test-Path -LiteralPath $rewritten) { [System.IO.File]::Delete($rewritten) }
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $rewritten -Force
        Move-Item -LiteralPath $rewritten -Destination $ArchivePath -Force
        $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
        try {
            if ($null -eq $archive.GetEntry('Snapture.ini')) {
                throw "Portable archive '$ArchivePath' is missing Snapture.ini."
            }
        }
        finally { $archive.Dispose() }
        Write-Host "    Added Snapture.ini portable marker to $(Split-Path -Leaf $ArchivePath)" -ForegroundColor Green
    }
    finally {
        if (Test-Path -LiteralPath $stage) { [System.IO.Directory]::Delete($stage, $true) }
        if (Test-Path -LiteralPath $rewritten) { [System.IO.File]::Delete($rewritten) }
    }
}

function Get-FileSha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Expand-ChocolateyTemplate {
    param(
        [string]$TemplatePath,
        [string]$DestinationPath,
        [hashtable]$Replacements
    )

    $content = [System.IO.File]::ReadAllText($TemplatePath)
    foreach ($replacement in $Replacements.GetEnumerator()) {
        $content = $content.Replace([string]$replacement.Key, [string]$replacement.Value)
    }
    if ($content -match '__[A-Z0-9_]+__') {
        throw "Unexpanded Chocolatey template token remains in '$DestinationPath'."
    }
    [System.IO.File]::WriteAllText($DestinationPath, $content, [System.Text.UTF8Encoding]::new($false))
}

function New-ChocolateyPackage {
    param(
        [string]$PackageId,
        [string]$NuspecTemplate,
        [string]$InstallTemplate,
        [string]$UninstallTemplate,
        [hashtable]$Replacements,
        [string]$VerificationText,
        [string]$OutputRoot,
        [string]$Version
    )

    $stage = Join-Path $OutputRoot "$PackageId-stage"
    if (Test-Path -LiteralPath $stage) { [System.IO.Directory]::Delete($stage, $true) }
    $tools = Join-Path $stage 'tools'
    New-Item -ItemType Directory -Path $tools -Force | Out-Null

    Expand-ChocolateyTemplate $NuspecTemplate (Join-Path $stage "$PackageId.nuspec") $Replacements
    Expand-ChocolateyTemplate $InstallTemplate (Join-Path $tools 'chocolateyInstall.ps1') $Replacements
    Expand-ChocolateyTemplate $UninstallTemplate (Join-Path $tools 'chocolateyUninstall.ps1') $Replacements
    [System.IO.File]::WriteAllText(
        (Join-Path $tools 'VERIFICATION.txt'),
        $VerificationText,
        [System.Text.UTF8Encoding]::new($false))

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $packagePath = Join-Path $OutputRoot "$PackageId.$Version.nupkg"
    if (Test-Path -LiteralPath $packagePath) { [System.IO.File]::Delete($packagePath) }
    $choco = Get-Command choco.exe -ErrorAction SilentlyContinue
    if ($null -ne $choco) {
        & $choco.Source pack (Join-Path $stage "$PackageId.nuspec") --outputdirectory $OutputRoot --no-progress
        if ($LASTEXITCODE -ne 0) { throw "Chocolatey pack failed for '$PackageId'." }
    }
    else {
        # The build remains usable on clean developer machines without Chocolatey;
        # a .nupkg is a NuGet-compatible zip with the nuspec at its root.
        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $stage,
            $packagePath,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false)
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        if (-not ($entries | Where-Object { $_ -match '\.nuspec$' })) { throw "Chocolatey package '$PackageId' is missing its nuspec." }
        foreach ($required in @('tools/chocolateyInstall.ps1', 'tools/chocolateyUninstall.ps1', 'tools/VERIFICATION.txt')) {
            if ($entries -notcontains $required) { throw "Chocolatey package '$PackageId' is missing '$required'." }
        }
    }
    finally { $archive.Dispose() }

    Write-Host "==> Wrote Chocolatey package $packagePath" -ForegroundColor Green
    return $packagePath
}

function New-ChocolateyPackages {
    $version = Get-ProjectVersion
    New-VelopackPackage -PackageRuntime 'win-x64'
    New-VelopackPackage -PackageRuntime 'win-arm64'
    $x64Root = Join-Path $root 'publish\velopack\win-x64'
    $arm64Root = Join-Path $root 'publish\velopack\win-arm64'

    $releaseBase = "https://github.com/SysAdminDoc/Snapture/releases/download/v$version"
    $assetNames = @{
        x64Setup = 'SysAdminDoc.Snapture-win-x64-stable-Setup.exe'
        arm64Setup = 'SysAdminDoc.Snapture-win-arm64-stable-Setup.exe'
        x64Portable = 'SysAdminDoc.Snapture-win-x64-stable-Portable.zip'
        arm64Portable = 'SysAdminDoc.Snapture-win-arm64-stable-Portable.zip'
    }
    $assetPaths = @{
        x64Setup = Join-Path $x64Root $assetNames.x64Setup
        arm64Setup = Join-Path $arm64Root $assetNames.arm64Setup
        x64Portable = Join-Path $x64Root $assetNames.x64Portable
        arm64Portable = Join-Path $arm64Root $assetNames.arm64Portable
    }
    foreach ($assetPath in $assetPaths.Values) {
        if (-not (Test-Path -LiteralPath $assetPath)) { throw "Chocolatey source asset is missing '$assetPath'." }
    }

    $replacements = @{
        '__VERSION__' = $version
        '__X64_URL__' = "$releaseBase/$($assetNames.x64Setup)"
        '__X64_CHECKSUM__' = Get-FileSha256 $assetPaths.x64Setup
        '__ARM64_URL__' = "$releaseBase/$($assetNames.arm64Setup)"
        '__ARM64_CHECKSUM__' = Get-FileSha256 $assetPaths.arm64Setup
    }
    $portableReplacements = @{
        '__VERSION__' = $version
        '__X64_URL__' = "$releaseBase/$($assetNames.x64Portable)"
        '__X64_CHECKSUM__' = Get-FileSha256 $assetPaths.x64Portable
        '__ARM64_URL__' = "$releaseBase/$($assetNames.arm64Portable)"
        '__ARM64_CHECKSUM__' = Get-FileSha256 $assetPaths.arm64Portable
    }

    $templateRoot = Join-Path $root 'packaging\chocolatey'
    $outputRoot = Join-Path $root 'publish\chocolatey'
    if (Test-Path -LiteralPath $outputRoot) { [System.IO.Directory]::Delete($outputRoot, $true) }
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

    $standardVerification = @"
Software: Snapture $version
Package: snapture
Release: $releaseBase
Download page: https://github.com/SysAdminDoc/Snapture/releases

32-bit URL: not supported
x64 URL: $($replacements.__X64_URL__)
x64 SHA256: $($replacements.__X64_CHECKSUM__)
ARM64 URL: $($replacements.__ARM64_URL__)
ARM64 SHA256: $($replacements.__ARM64_CHECKSUM__)
Checksum type: SHA256
"@
    $portableVerification = @"
Software: Snapture $version
Package: snapture.portable
Release: $releaseBase
Download page: https://github.com/SysAdminDoc/Snapture/releases

32-bit URL: not supported
x64 URL: $($portableReplacements.__X64_URL__)
x64 SHA256: $($portableReplacements.__X64_CHECKSUM__)
ARM64 URL: $($portableReplacements.__ARM64_URL__)
ARM64 SHA256: $($portableReplacements.__ARM64_CHECKSUM__)
Checksum type: SHA256
"@

    $standardPackage = New-ChocolateyPackage `
        -PackageId 'snapture' `
        -NuspecTemplate (Join-Path $templateRoot 'snapture.nuspec.template') `
        -InstallTemplate (Join-Path $templateRoot 'snapture.install.ps1.template') `
        -UninstallTemplate (Join-Path $templateRoot 'snapture.uninstall.ps1.template') `
        -Replacements $replacements `
        -VerificationText $standardVerification `
        -OutputRoot $outputRoot `
        -Version $version
    $portablePackage = New-ChocolateyPackage `
        -PackageId 'snapture.portable' `
        -NuspecTemplate (Join-Path $templateRoot 'snapture.portable.nuspec.template') `
        -InstallTemplate (Join-Path $templateRoot 'snapture.portable.install.ps1.template') `
        -UninstallTemplate (Join-Path $templateRoot 'snapture.portable.uninstall.ps1.template') `
        -Replacements $portableReplacements `
        -VerificationText $portableVerification `
        -OutputRoot $outputRoot `
        -Version $version

    [System.IO.Directory]::Delete((Join-Path $outputRoot 'snapture-stage'), $true)
    [System.IO.Directory]::Delete((Join-Path $outputRoot 'snapture.portable-stage'), $true)
    Write-Host "    Standard: $standardPackage" -ForegroundColor Green
    Write-Host "    Portable: $portablePackage" -ForegroundColor Green
}

function New-NuGetPackage {
    $outputRoot = Join-Path $root 'publish\nuget'
    if (Test-Path -LiteralPath $outputRoot) { [System.IO.Directory]::Delete($outputRoot, $true) }
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    Write-Host '==> dotnet pack Snapture.Plugin.Abstractions' -ForegroundColor Cyan
    dotnet pack "$root\src\Snapture.Plugin.Abstractions\Snapture.Plugin.Abstractions.csproj" `
        -c $Configuration `
        --no-restore `
        -o $outputRoot `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw 'NuGet package build failed.' }

    $version = Get-ProjectVersion
    foreach ($artifact in @(
        (Join-Path $outputRoot "Snapture.Plugin.Abstractions.$version.nupkg"),
        (Join-Path $outputRoot "Snapture.Plugin.Abstractions.$version.snupkg"))) {
        if (-not (Test-Path -LiteralPath $artifact)) { throw "NuGet build is missing '$artifact'." }
        if ((Get-Item -LiteralPath $artifact).Length -le 0) { throw "NuGet build produced an empty '$artifact'." }
    }

    $package = [System.IO.Compression.ZipFile]::OpenRead(
        (Join-Path $outputRoot "Snapture.Plugin.Abstractions.$version.nupkg"))
    try {
        if ($null -eq $package.GetEntry('README.md')) { throw 'NuGet package is missing README.md.' }
        if ($null -eq $package.GetEntry("lib/netstandard2.0/Snapture.Plugin.Abstractions.dll")) {
            throw 'NuGet package is missing the netstandard2.0 assembly.'
        }
        if ($null -eq $package.GetEntry("lib/net10.0/Snapture.Plugin.Abstractions.dll")) {
            throw 'NuGet package is missing the net10.0 assembly.'
        }
    }
    finally { $package.Dispose() }
    Write-Host "==> Wrote local NuGet package artifacts to $outputRoot" -ForegroundColor Green
    Write-Host '    Publication intentionally remains operator-controlled; no API key is used by this build.' -ForegroundColor Yellow
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
        $zipPath = Join-Path $root "publish\Snapture-v$version-$Runtime.zip"
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        Compress-Archive -Path "$out\*" -DestinationPath $zipPath -Force
        Add-PortableMarkerToArchive $zipPath
        $sha = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower()
        Set-Content -Path "$zipPath.sha256" -Value "$sha  $(Split-Path -Leaf $zipPath)"
        Write-Host "==> Wrote $zipPath" -ForegroundColor Green
        Write-Host "    SHA256 $sha" -ForegroundColor Green
    }
}

if ($Msix) { New-MsixPackage }
if ($Msi) { New-MsiPackage }
if ($Velopack -and -not $Chocolatey) { New-VelopackPackage }
if ($Chocolatey) { New-ChocolateyPackages }
if ($NuGet) { New-NuGetPackage }

Write-Host "==> Done." -ForegroundColor Green
