[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string[]]$Runtime = @('win-x64', 'win-arm64'),
    [string]$PublishRoot,
    [string]$OutputRoot,
    [string]$ArtifactRoot,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
$script:Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($PublishRoot)) { $PublishRoot = Join-Path $script:Root 'publish' }
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $PublishRoot 'sbom' }

# These are local release floors. They intentionally track the versions that have
# passed the project's security review; changing one is a security-sensitive code
# review, not an online advisory lookup at release time.
$script:SecurityFloors = [ordered]@{
    SQLite = [ordered]@{ Label = 'SQLite'; Minimum = '3.53.4'; Package = 'SQLite'; Source = 'SQLite native runtime floor' }
    MagickNet = [ordered]@{ Label = 'Magick.NET'; Minimum = '14.15.0'; PackagePattern = '^Magick\.NET-Q8-(x64|arm64)$'; Source = 'Magick.NET package floor' }
    ImageMagickNative = [ordered]@{ Label = 'ImageMagick native'; Minimum = '7.1.2'; FilePattern = '^Magick\.Native-Q8-(x64|arm64)\.dll$'; Source = 'ImageMagick native codec floor' }
    WindowsAppSdkAi = [ordered]@{ Label = 'Windows App SDK AI'; Minimum = '1.8.76'; Package = 'Microsoft.WindowsAppSDK.AI'; Source = 'Windows App SDK AI floor' }
    WindowsAppSdkFoundation = [ordered]@{ Label = 'Windows App SDK Foundation'; Minimum = '1.8.260505001'; Package = 'Microsoft.WindowsAppSDK.Foundation'; Source = 'Windows App SDK Foundation floor' }
    WindowsAppSdkRuntime = [ordered]@{ Label = 'Windows App SDK Runtime'; Minimum = '1.8.260508005'; Package = 'Microsoft.WindowsAppSDK.Runtime'; Source = 'Windows App SDK Runtime floor' }
    OnnxRuntime = [ordered]@{ Label = 'ONNX Runtime'; Minimum = '1.27.1'; Package = 'Microsoft.ML.OnnxRuntime'; Source = 'ONNX Runtime package floor' }
    OnnxRuntimeManaged = [ordered]@{ Label = 'ONNX Runtime managed'; Minimum = '1.27.1'; Package = 'Microsoft.ML.OnnxRuntime.Managed'; Source = 'ONNX Runtime managed package floor' }
    SkiaSharp = [ordered]@{ Label = 'SkiaSharp'; Minimum = '3.119.2'; Package = 'SkiaSharp'; Source = 'SkiaSharp package floor' }
    SkiaSharpNative = [ordered]@{ Label = 'SkiaSharp native'; Minimum = '3.119.2'; Package = 'SkiaSharp.NativeAssets.Win32'; FilePattern = '^libSkiaSharp\.dll$'; Source = 'SkiaSharp native codec floor' }
    DotNetCore = [ordered]@{ Label = '.NET Core runtime'; Minimum = '10.0.10'; Runtime = 'Microsoft.NETCore.App'; Source = '.NET runtime floor' }
    DotNetDesktop = [ordered]@{ Label = '.NET Windows Desktop runtime'; Minimum = '10.0.10'; Runtime = 'Microsoft.WindowsDesktop.App'; Source = '.NET Windows Desktop runtime floor' }
    DotNetAspNet = [ordered]@{ Label = '.NET ASP.NET runtime'; Minimum = '10.0.10'; Runtime = 'Microsoft.AspNetCore.App'; Source = '.NET ASP.NET runtime floor' }
}

$script:LicenseByPackage = @{
    'Clipper2' = 'BSL-1.0'
    'CommunityToolkit.Mvvm' = 'MIT'
    'DocumentFormat.OpenXml' = 'MIT'
    'DocumentFormat.OpenXml.Framework' = 'MIT'
    'Hardcodet.NotifyIcon.Wpf' = 'MIT'
    'Magick.NET-Q8-x64' = 'Apache-2.0'
    'Magick.NET-Q8-arm64' = 'Apache-2.0'
    'Magick.NET.Core' = 'Apache-2.0'
    'Microsoft.Data.Sqlite' = 'MIT'
    'Microsoft.Data.Sqlite.Core' = 'MIT'
    'Microsoft.ML.OnnxRuntime' = 'MIT'
    'Microsoft.ML.OnnxRuntime.Managed' = 'MIT'
    'Microsoft.WindowsAppSDK.AI' = 'MIT'
    'Microsoft.WindowsAppSDK.Base' = 'MIT'
    'Microsoft.WindowsAppSDK.Foundation' = 'MIT'
    'Microsoft.WindowsAppSDK.InteractiveExperiences' = 'MIT'
    'Microsoft.WindowsAppSDK.Runtime' = 'MIT'
    'Microsoft.Windows.SDK.BuildTools' = 'MIT'
    'Microsoft.Windows.SDK.BuildTools.MSIX' = 'MIT'
    'NAudio' = 'MIT'
    'NAudio.Asio' = 'MIT'
    'NAudio.Core' = 'MIT'
    'NAudio.Midi' = 'MIT'
    'NAudio.Wasapi' = 'MIT'
    'NAudio.WinForms' = 'MIT'
    'NAudio.WinMM' = 'MIT'
    'OpenTK' = 'MIT'
    'OpenTK.Compute' = 'MIT'
    'OpenTK.Core' = 'MIT'
    'OpenTK.GLWpfControl' = 'MIT'
    'OpenTK.Graphics' = 'MIT'
    'OpenTK.Input' = 'MIT'
    'OpenTK.Mathematics' = 'MIT'
    'OpenTK.OpenAL' = 'MIT'
    'OpenTK.redist.glfw' = 'MIT'
    'OpenTK.Windowing.Common' = 'MIT'
    'OpenTK.Windowing.Desktop' = 'MIT'
    'OpenTK.Windowing.GraphicsLibraryFramework' = 'MIT'
    'RapidOcrNet' = 'MIT'
    'Serilog' = 'Apache-2.0'
    'Serilog.Sinks.File' = 'Apache-2.0'
    'SkiaSharp' = 'MIT'
    'SkiaSharp.NativeAssets.Linux' = 'MIT'
    'SkiaSharp.NativeAssets.Win32' = 'MIT'
    'SkiaSharp.Svg' = 'MIT'
    'SkiaSharp.Views.Desktop.Common' = 'MIT'
    'SkiaSharp.Views.WPF' = 'MIT'
    'SQLite' = 'blessing'
    'SQLitePCLRaw.bundle_e_sqlite3' = 'Apache-2.0'
    'SQLitePCLRaw.config.e_sqlite3' = 'Apache-2.0'
    'SQLitePCLRaw.core' = 'Apache-2.0'
    'SQLitePCLRaw.provider.e_sqlite3' = 'Apache-2.0'
    'System.Drawing.Common' = 'MIT'
    'System.Numerics.Tensors' = 'MIT'
    'Velopack' = 'MIT'
    'ZXing.Net' = 'Apache-2.0'
}

function Write-Utf8NoBom {
    param([string]$Path, [string]$Content)

    $parent = Split-Path -Parent $Path
    if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function ConvertTo-JsonFile {
    param([string]$Path, [object]$Value)

    Write-Utf8NoBom $Path (($Value | ConvertTo-Json -Depth 30) + "`n")
}

function Get-TextSha256 {
    param([string]$Text)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([Convert]::ToHexString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Text)))).ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Get-FileSha256 {
    param([string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-VersionParts {
    param([string]$Version)

    if ([string]::IsNullOrWhiteSpace($Version)) { return $null }
    $match = [regex]::Match($Version, '(?<version>\d+(?:\.\d+){0,3})')
    if (-not $match.Success) { return $null }
    $parts = [int64[]]($match.Groups['version'].Value -split '\.' | ForEach-Object { [int64]$_ })
    while ($parts.Count -lt 4) { $parts += [int64]0 }
    return $parts
}

function Compare-Version {
    param([string]$Left, [string]$Right)

    $leftParts = Get-VersionParts $Left
    $rightParts = Get-VersionParts $Right
    if ($null -eq $leftParts -or $null -eq $rightParts) { throw "Cannot compare versions '$Left' and '$Right'." }
    for ($index = 0; $index -lt 4; $index++) {
        if ($leftParts[$index] -lt $rightParts[$index]) { return -1 }
        if ($leftParts[$index] -gt $rightParts[$index]) { return 1 }
    }
    return 0
}

function Test-VersionAtLeast {
    param([string]$Actual, [string]$Minimum)

    if ([string]::IsNullOrWhiteSpace($Actual)) { return $false }
    return (Compare-Version $Actual $Minimum) -ge 0
}

function Get-ProjectVersion {
    $path = Join-Path $script:Root 'src\Snapture.App\Snapture.App.csproj'
    $match = Select-String -LiteralPath $path -Pattern '<Version>([^<]+)</Version>'
    if ($null -eq $match) { throw "Could not read the application version from '$path'." }
    return $match.Matches[0].Groups[1].Value
}

function Get-DirectPackageReferences {
    param([string]$PackageRuntime)

    $references = @{}
    foreach ($project in @('src\Snapture.App\Snapture.App.csproj', 'src\Snapture.Capture\Snapture.Capture.csproj')) {
        $path = Join-Path $script:Root $project
        [xml]$xml = Get-Content -LiteralPath $path -Raw
        foreach ($reference in $xml.Project.ItemGroup.PackageReference) {
            $name = [string]$reference.Include
            $version = [string]$reference.Version
            if ([string]::IsNullOrWhiteSpace($version) -and $null -ne $reference.Version) { $version = [string]$reference.Version.'#text' }
            if ($name -eq 'Magick.NET-Q8-x64' -and $PackageRuntime -ne 'win-x64') { continue }
            if ($name -eq 'Magick.NET-Q8-arm64' -and $PackageRuntime -ne 'win-arm64') { continue }
            if (-not [string]::IsNullOrWhiteSpace($name) -and -not [string]::IsNullOrWhiteSpace($version)) {
                $references[$name] = $version
            }
        }
    }
    return $references
}

function Add-ResolvedPackage {
    param([hashtable]$Packages, [string]$Key)

    $parts = $Key -split '/', 2
    if ($parts.Count -ne 2) { return }
    $name = $parts[0]
    $version = $parts[1]
    if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($version)) { return }
    if ($name -in @('Snapture.App', 'Snapture.Capture', 'Snapture.Plugin.Abstractions')) { return }
    $Packages[$name] = [ordered]@{ Name = $name; Version = $version; Source = 'resolved dependency graph' }
}

function Get-ResolvedPackages {
    param([string]$PackageRuntime, [string]$PayloadRoot)

    $packages = @{}
    $depsPath = Join-Path $PayloadRoot 'Snapture.App.deps.json'
    if (Test-Path -LiteralPath $depsPath) {
        $deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json
        foreach ($property in $deps.libraries.PSObject.Properties) {
            if ([string]$property.Value.type -eq 'package' -or [string]$property.Name -notmatch '^Snapture\.') {
                Add-ResolvedPackage $packages $property.Name
            }
        }
    }

    $assetsPath = Join-Path $script:Root 'src\Snapture.App\obj\project.assets.json'
    if (Test-Path -LiteralPath $assetsPath) {
        $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
        foreach ($property in $assets.libraries.PSObject.Properties) {
            Add-ResolvedPackage $packages $property.Name
        }
    }

    $direct = Get-DirectPackageReferences $PackageRuntime
    foreach ($name in @('Magick.NET-Q8-x64', 'Magick.NET-Q8-arm64')) { $packages.Remove($name) }
    foreach ($reference in $direct.GetEnumerator()) {
        if ($packages.ContainsKey($reference.Key) -and $packages[$reference.Key].Version -ne $reference.Value) {
            throw "Resolved package '$($reference.Key)' is $($packages[$reference.Key].Version), but the project declares $($reference.Value). Restore/build metadata is stale."
        }
        $packages[$reference.Key] = [ordered]@{ Name = $reference.Key; Version = $reference.Value; Source = 'project package reference' }
    }

    if (-not $packages.ContainsKey('SQLite')) { throw "Resolved dependency graph does not contain the native SQLite package." }
    $selectedMagick = "Magick.NET-Q8-$(if ($PackageRuntime -eq 'win-arm64') { 'arm64' } else { 'x64' })"
    if (-not $packages.ContainsKey($selectedMagick)) { throw "Resolved dependency graph does not contain '$selectedMagick'." }
    return @($packages.Values | Sort-Object Name)
}

function Get-FileVersionMetadata {
    param([string]$Path)

    try {
        $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
        return [ordered]@{ FileVersion = [string]$info.FileVersion; ProductVersion = [string]$info.ProductVersion }
    }
    catch { return [ordered]@{ FileVersion = ''; ProductVersion = '' } }
}

function Get-ArtifactManifest {
    param([string]$Root)

    $rootPath = (Resolve-Path -LiteralPath $Root).Path
    $items = @(Get-ChildItem -LiteralPath $rootPath -File -Recurse -Force | Sort-Object FullName)
    if ($items.Count -eq 0) { throw "Artifact root '$rootPath' contains no files." }
    $files = @(
        foreach ($item in $items) {
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Artifact contains a reparse-point file '$($item.FullName)'."
            }
            $relative = [System.IO.Path]::GetRelativePath($rootPath, $item.FullName).Replace('\', '/')
            $metadata = Get-FileVersionMetadata $item.FullName
            [ordered]@{
                Path = $relative
                Size = [int64]$item.Length
                Sha256 = Get-FileSha256 $item.FullName
                FileVersion = $metadata.FileVersion
                ProductVersion = $metadata.ProductVersion
            }
        }
    )
    $canonical = (($files | ForEach-Object { "$($_.Path)|$($_.Size)|$($_.Sha256)" }) -join "`n") + "`n"
    return [ordered]@{
        SchemaVersion = 1
        ArtifactRoot = 'release-payload'
        ArtifactSha256 = Get-TextSha256 $canonical
        Files = $files
        Canonical = $canonical
    }
}

function Test-ArtifactManifest {
    param([string]$Root, [object]$Expected)

    $actual = Get-ArtifactManifest $Root
    if ($actual.ArtifactSha256 -ne $Expected.ArtifactSha256) {
        throw "Artifact changed after manifest generation (expected $($Expected.ArtifactSha256), found $($actual.ArtifactSha256))."
    }
    $expectedFiles = @($Expected.Files)
    $actualFiles = @($actual.Files)
    if ($expectedFiles.Count -ne $actualFiles.Count) { throw 'Artifact file count changed after manifest generation.' }
    for ($index = 0; $index -lt $expectedFiles.Count; $index++) {
        if ($expectedFiles[$index].Path -ne $actualFiles[$index].Path -or $expectedFiles[$index].Sha256 -ne $actualFiles[$index].Sha256) {
            throw "Artifact file '$($expectedFiles[$index].Path)' changed after manifest generation."
        }
    }
    return $true
}

function Get-ArtifactFileMap {
    param([object]$Manifest)

    $map = @{}
    foreach ($file in @($Manifest.Files)) {
        $map[$file.Path.ToLowerInvariant()] = $file
    }
    return $map
}

function Find-ArtifactFiles {
    param([hashtable]$FileMap, [string]$LeafPattern)

    return @($FileMap.Values | Where-Object { (Split-Path -Leaf $_.Path) -match $LeafPattern } | Sort-Object Path)
}

function Find-ArtifactFile {
    param([hashtable]$FileMap, [string]$LeafName)

    return @($FileMap.Values | Where-Object { (Split-Path -Leaf $_.Path) -ieq $LeafName } | Select-Object -First 1)
}

function Get-PackageFiles {
    param([string]$PackageName, [hashtable]$FileMap)

    $patterns = @()
    switch -Regex ($PackageName) {
        '^Magick\.NET-Q8-' { $patterns += '^Magick\.NET-Q8-.*\.dll$'; $patterns += '^Magick\.NET\.Core\.dll$'; break }
        '^Microsoft\.ML\.OnnxRuntime$' { $patterns += '^Microsoft\.ML\.OnnxRuntime\.dll$'; break }
        '^Microsoft\.ML\.OnnxRuntime\.Managed$' { $patterns += '^Microsoft\.ML\.OnnxRuntime\.Managed\.dll$'; break }
        '^Microsoft\.WindowsAppSDK' { $patterns += '^Microsoft\.Windows.*\.dll$'; break }
        '^SkiaSharp$' { $patterns += '^SkiaSharp\.dll$'; break }
        '^SkiaSharp\.Views\.WPF$' { $patterns += '^SkiaSharp\.Views\.WPF\.dll$'; break }
        '^SkiaSharp\.Views\.Desktop\.Common$' { $patterns += '^SkiaSharp\.Views\.Desktop\.Common\.dll$'; break }
        '^SkiaSharp\.Svg$' { $patterns += '^SkiaSharp\.Extended\.Svg\.dll$'; break }
        '^SQLitePCLRaw\.' { $patterns += "^$([regex]::Escape($PackageName))\.dll$"; break }
        '^ZXing\.Net$' { $patterns += '^zxing\.dll$'; break }
        default { $patterns += "^$([regex]::Escape($PackageName))\.dll$"; break }
    }
    $files = @()
    foreach ($pattern in $patterns) { $files += Find-ArtifactFiles $FileMap $pattern }
    return @($files | Sort-Object Path -Unique)
}

function Get-LicenseId {
    param([string]$PackageName)

    if ($script:LicenseByPackage.ContainsKey($PackageName)) { return $script:LicenseByPackage[$PackageName] }
    return 'NOASSERTION'
}

function Get-LicenseDescriptor {
    param([string]$License)

    if ($License -eq 'blessing') { return [ordered]@{ name = 'SQLite blessing' } }
    return [ordered]@{ id = $License }
}

function Get-DotNetRuntimeVersions {
    $lines = @(& dotnet --list-runtimes 2>$null)
    $versions = @()
    foreach ($line in $lines) {
        $match = [regex]::Match([string]$line, '^(?<name>\S+)\s+(?<version>\d+(?:\.\d+){1,3})\s+')
        if ($match.Success) {
            $versions += [ordered]@{ Name = $match.Groups['name'].Value; Version = $match.Groups['version'].Value }
        }
    }
    return $versions
}

function Add-FloorResult {
    param(
        [System.Collections.Generic.List[object]]$Results,
        [string]$Id,
        [string]$Label,
        [string]$Actual,
        [string]$Minimum,
        [string]$Source,
        [string[]]$Evidence,
        [string]$VersionSource = ''
    )

    $status = if (Test-VersionAtLeast $Actual $Minimum) { 'pass' } else { 'fail' }
    $Results.Add([ordered]@{
        Id = $Id
        Label = $Label
        Actual = $Actual
        Minimum = $Minimum
        Status = $status
        Source = $Source
        VersionSource = $VersionSource
        Evidence = @($Evidence)
    })
}

function Invoke-SecurityFloorChecks {
    param(
        [string]$PackageRuntime,
        [object[]]$Packages,
        [hashtable]$FileMap,
        [object[]]$DotNetRuntimeVersions
    )

    $results = [System.Collections.Generic.List[object]]::new()
    $packageMap = @{}
    foreach ($package in $Packages) { $packageMap[$package.Name] = $package }

    $sqlite = $packageMap['SQLite']
    Add-FloorResult $results 'sqlite-package' $script:SecurityFloors.SQLite.Label $sqlite.Version $script:SecurityFloors.SQLite.Minimum $script:SecurityFloors.SQLite.Source @('Snapture.App.deps.json', 'e_sqlite3.dll') 'resolved package version'

    $magickName = "Magick.NET-Q8-$(if ($PackageRuntime -eq 'win-arm64') { 'arm64' } else { 'x64' })"
    $magick = $packageMap[$magickName]
    Add-FloorResult $results 'magick-net-package' $script:SecurityFloors.MagickNet.Label $magick.Version $script:SecurityFloors.MagickNet.Minimum $script:SecurityFloors.MagickNet.Source @('Snapture.App.deps.json', $magickName + '.dll') 'resolved package version'
    $magickNative = Find-ArtifactFiles $FileMap $script:SecurityFloors.ImageMagickNative.FilePattern | Select-Object -First 1
    $magickNativeVersion = if ($magickNative.FileVersion) { $magickNative.FileVersion } else { $magick.Version }
    $magickNativeSource = if ($magickNative.FileVersion) { 'PE file version' } else { 'resolved Magick.NET package version; native file has no PE version resource' }
    Add-FloorResult $results 'imagemagick-native' $script:SecurityFloors.ImageMagickNative.Label $magickNativeVersion $script:SecurityFloors.ImageMagickNative.Minimum $script:SecurityFloors.ImageMagickNative.Source @($magickNative.Path) $magickNativeSource

    foreach ($check in @(
        @{ Id = 'windows-app-sdk-ai'; Floor = $script:SecurityFloors.WindowsAppSdkAi },
        @{ Id = 'windows-app-sdk-foundation'; Floor = $script:SecurityFloors.WindowsAppSdkFoundation },
        @{ Id = 'windows-app-sdk-runtime'; Floor = $script:SecurityFloors.WindowsAppSdkRuntime },
        @{ Id = 'onnx-runtime'; Floor = $script:SecurityFloors.OnnxRuntime },
        @{ Id = 'onnx-runtime-managed'; Floor = $script:SecurityFloors.OnnxRuntimeManaged },
        @{ Id = 'skiasharp'; Floor = $script:SecurityFloors.SkiaSharp }
    )) {
        $package = $packageMap[$check.Floor.Package]
        Add-FloorResult $results $check.Id $check.Floor.Label $package.Version $check.Floor.Minimum $check.Floor.Source @('Snapture.App.deps.json', $check.Floor.Package) 'resolved package version'
    }

    $skiaNative = Find-ArtifactFiles $FileMap $script:SecurityFloors.SkiaSharpNative.FilePattern | Select-Object -First 1
    $skiaNativePackage = $packageMap[$script:SecurityFloors.SkiaSharpNative.Package]
    $skiaNativeVersion = if ($skiaNative.FileVersion) { $skiaNative.FileVersion } else { $skiaNativePackage.Version }
    $skiaNativeSource = if ($skiaNative.FileVersion) { 'PE file version' } else { 'resolved SkiaSharp native package version; native file has no PE version resource' }
    Add-FloorResult $results 'skiasharp-native' $script:SecurityFloors.SkiaSharpNative.Label $skiaNativeVersion $script:SecurityFloors.SkiaSharpNative.Minimum $script:SecurityFloors.SkiaSharpNative.Source @($skiaNative.Path) $skiaNativeSource

    foreach ($check in @(
        @{ Id = 'dotnet-core-runtime'; Floor = $script:SecurityFloors.DotNetCore },
        @{ Id = 'dotnet-desktop-runtime'; Floor = $script:SecurityFloors.DotNetDesktop },
        @{ Id = 'dotnet-aspnet-runtime'; Floor = $script:SecurityFloors.DotNetAspNet }
    )) {
        $runtime = @($DotNetRuntimeVersions | Where-Object { $_.Name -eq $check.Floor.Runtime } | Sort-Object Version | Select-Object -Last 1)
        $runtimeVersion = if ($runtime.Count -gt 0) { $runtime[0].Version } else { '' }
        Add-FloorResult $results $check.Id $check.Floor.Label $runtimeVersion $check.Floor.Minimum $check.Floor.Source @('dotnet --list-runtimes') 'installed runtime version'
    }

    return @($results)
}

function Assert-RequiredArtifactFiles {
    param([string]$PackageRuntime, [hashtable]$FileMap)

    $suffix = if ($PackageRuntime -eq 'win-arm64') { 'arm64' } else { 'x64' }
    $required = @(
        'Snapture.App.exe',
        'Snapture.App.dll',
        'Snapture.App.deps.json',
        'Snapture.App.runtimeconfig.json',
        'e_sqlite3.dll',
        "Magick.NET-Q8-$suffix.dll",
        "Magick.Native-Q8-$suffix.dll",
        'Microsoft.Data.Sqlite.dll',
        'Microsoft.ML.OnnxRuntime.dll',
        'onnxruntime.dll',
        'onnxruntime_providers_shared.dll',
        'RapidOcrNet.dll',
        'SkiaSharp.dll',
        'SkiaSharp.Views.WPF.dll',
        'libSkiaSharp.dll'
    )
    $missing = @($required | Where-Object { $null -eq (Find-ArtifactFile $FileMap $_) })
    if ($missing.Count -gt 0) { throw "The $PackageRuntime release payload is missing required runtime files: $($missing -join ', ')" }
}

function New-PackageComponent {
    param([object]$Package, [hashtable]$FileMap)

    $files = @(Get-PackageFiles $Package.Name $FileMap)
    $license = Get-LicenseId $Package.Name
    $properties = @(
        [ordered]@{ Name = 'snapture:source'; Value = $Package.Source },
        [ordered]@{ Name = 'snapture:artifactFiles'; Value = (($files | ForEach-Object Path) -join ',') },
        [ordered]@{ Name = 'snapture:licenseEvidence'; Value = if ($license -eq 'NOASSERTION') { 'No local SPDX mapping; review before distribution.' } else { 'Local package license inventory.' } }
    )
    $component = [ordered]@{
        type = 'library'
        'bom-ref' = "pkg:nuget/$($Package.Name)@$($Package.Version)"
        name = $Package.Name
        version = $Package.Version
        purl = "pkg:nuget/$($Package.Name)@$($Package.Version)"
        licenses = @([ordered]@{ license = (Get-LicenseDescriptor $license) })
        properties = @($properties | ForEach-Object { [ordered]@{ name = $_.Name; value = $_.Value } })
    }
    if ($files.Count -gt 0) {
        $component.hashes = @($files | ForEach-Object { [ordered]@{ alg = 'SHA-256'; content = $_.Sha256 } })
    }
    return $component
}

function New-NativeComponent {
    param([string]$BomRef, [string]$Name, [string]$Version, [string]$Package, [object]$File)

    return [ordered]@{
        type = 'library'
        'bom-ref' = $BomRef
        name = $Name
        version = $Version
        licenses = @([ordered]@{ license = [ordered]@{ id = 'NOASSERTION' } })
        hashes = @([ordered]@{ alg = 'SHA-256'; content = $File.Sha256 })
        properties = @(
            [ordered]@{ name = 'snapture:source'; value = 'actual release payload' },
            [ordered]@{ name = 'snapture:artifactFiles'; value = $File.Path },
            [ordered]@{ name = 'snapture:package'; value = $Package },
            [ordered]@{ name = 'snapture:fileVersion'; value = $File.FileVersion },
            [ordered]@{ name = 'snapture:licenseEvidence'; value = 'Native component license requires package review; no SPDX assertion is inferred from a binary.' }
        )
    }
}

function New-Sbom {
    param(
        [string]$PackageRuntime,
        [string]$Version,
        [object]$Manifest,
        [object[]]$Packages,
        [hashtable]$FileMap,
        [object[]]$DotNetRuntimeVersions,
        [string]$ManifestFileName,
        [string]$FloorReportFileName,
        [string]$LicenseReportFileName
    )

    $components = [System.Collections.Generic.List[object]]::new()
    $appFiles = @('Snapture.App.exe', 'Snapture.App.dll', 'Snapture.App.deps.json', 'Snapture.App.runtimeconfig.json' | ForEach-Object { Find-ArtifactFile $FileMap $_ })
    $components.Add([ordered]@{
        type = 'application'
        'bom-ref' = "application:Snapture:${Version}:$PackageRuntime"
        name = 'Snapture'
        version = $Version
        licenses = @([ordered]@{ license = [ordered]@{ id = 'MIT' } })
        hashes = @($appFiles | ForEach-Object { [ordered]@{ alg = 'SHA-256'; content = $_.Sha256 } })
        properties = @(
            [ordered]@{ name = 'snapture:architecture'; value = $PackageRuntime },
            [ordered]@{ name = 'snapture:artifactFiles'; value = (($appFiles | ForEach-Object Path) -join ',') },
            [ordered]@{ name = 'snapture:artifactManifest'; value = $ManifestFileName },
            [ordered]@{ name = 'snapture:artifactSha256'; value = $Manifest.ArtifactSha256 }
        )
    })

    foreach ($package in @($Packages | Sort-Object Name)) { $components.Add((New-PackageComponent $package $FileMap)) }

    $magickFile = Find-ArtifactFiles $FileMap '^Magick\.Native-Q8-(x64|arm64)\.dll$' | Select-Object -First 1
    $sqliteFile = Find-ArtifactFile $FileMap 'e_sqlite3.dll'
    $onnxFile = Find-ArtifactFile $FileMap 'onnxruntime.dll'
    $skiaFile = Find-ArtifactFile $FileMap 'libSkiaSharp.dll'
    $magickPackage = @($Packages | Where-Object Name -match '^Magick\.NET-Q8-') | Select-Object -First 1
    $sqlitePackage = @($Packages | Where-Object Name -eq 'SQLite') | Select-Object -First 1
    $onnxPackage = @($Packages | Where-Object Name -eq 'Microsoft.ML.OnnxRuntime') | Select-Object -First 1
    $skiaPackage = @($Packages | Where-Object Name -eq 'SkiaSharp.NativeAssets.Win32') | Select-Object -First 1
    if ($null -ne $sqliteFile) { $components.Add((New-NativeComponent 'native:sqlite' 'SQLite native runtime' $sqlitePackage.Version 'SQLite' $sqliteFile)) }
    if ($null -ne $magickFile) { $components.Add((New-NativeComponent 'native:imagemagick' 'ImageMagick native codec runtime' $(if ($magickFile.FileVersion) { $magickFile.FileVersion } else { $magickPackage.Version }) $magickPackage.Name $magickFile)) }
    if ($null -ne $onnxFile) { $components.Add((New-NativeComponent 'native:onnxruntime' 'ONNX Runtime native runtime' $(if ($onnxFile.FileVersion) { $onnxFile.FileVersion } else { $onnxPackage.Version }) $onnxPackage.Name $onnxFile)) }
    if ($null -ne $skiaFile) { $components.Add((New-NativeComponent 'native:skiasharp' 'SkiaSharp native runtime' $(if ($skiaFile.FileVersion) { $skiaFile.FileVersion } else { $skiaPackage.Version }) $skiaPackage.Name $skiaFile)) }

    foreach ($runtimeName in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App', 'Microsoft.AspNetCore.App')) {
        $runtime = @($DotNetRuntimeVersions | Where-Object Name -eq $runtimeName | Sort-Object Version | Select-Object -Last 1)
        if ($runtime.Count -gt 0) {
            $components.Add([ordered]@{
                type = 'framework'
                'bom-ref' = "framework:${runtimeName}:$($runtime[0].Version)"
                name = $runtimeName
                version = $runtime[0].Version
                licenses = @([ordered]@{ license = [ordered]@{ id = 'MIT' } })
                properties = @([ordered]@{ name = 'snapture:source'; value = 'dotnet --list-runtimes' })
            })
        }
    }

    foreach ($model in @($Manifest.Files | Where-Object { $_.Path -match '\.onnx$' } | Sort-Object Path)) {
        $components.Add([ordered]@{
            type = 'machine-learning-model'
            'bom-ref' = "model:$($model.Path)"
            name = (Split-Path -Leaf $model.Path)
            version = 'bundled'
            hashes = @([ordered]@{ alg = 'SHA-256'; content = $model.Sha256 })
            licenses = @([ordered]@{ license = [ordered]@{ id = 'NOASSERTION' } })
            properties = @([ordered]@{ name = 'snapture:artifactFiles'; value = $model.Path })
        })
    }

    $orderedComponents = @($components | Sort-Object type, name, version, 'bom-ref')
    $guid = $Manifest.ArtifactSha256.Substring(0, 32)
    $serial = "urn:uuid:$($guid.Substring(0, 8))-$($guid.Substring(8, 4))-$($guid.Substring(12, 4))-$($guid.Substring(16, 4))-$($guid.Substring(20, 12))"
    return [ordered]@{
        bomFormat = 'CycloneDX'
        specVersion = '1.5'
        serialNumber = $serial
        version = 1
        metadata = [ordered]@{
            component = [ordered]@{ type = 'application'; name = 'Snapture'; version = $Version }
            properties = @(
                [ordered]@{ name = 'snapture:architecture'; value = $PackageRuntime },
                [ordered]@{ name = 'snapture:artifactManifest'; value = $ManifestFileName },
                [ordered]@{ name = 'snapture:artifactSha256'; value = $Manifest.ArtifactSha256 },
                [ordered]@{ name = 'snapture:securityFloorReport'; value = $FloorReportFileName },
                [ordered]@{ name = 'snapture:licenseReport'; value = $LicenseReportFileName },
                [ordered]@{ name = 'snapture:offlineVerification'; value = 'true' }
            )
        }
        components = $orderedComponents
    }
}

function Resolve-ArtifactRoot {
    param([string]$PackageRuntime, [string]$ExplicitRoot)

    $cleanup = $null
    if (-not [string]::IsNullOrWhiteSpace($ExplicitRoot)) {
        $candidate = (Resolve-Path -LiteralPath $ExplicitRoot).Path
    }
    else {
        $candidates = @(
            (Join-Path $PublishRoot "velopack\$PackageRuntime\payload"),
            (Join-Path $PublishRoot $PackageRuntime)
        )
        $candidate = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1
        if ($null -eq $candidate) { throw "No release payload found for $PackageRuntime. Build or publish it first, or pass -ArtifactRoot." }
        $candidate = (Resolve-Path -LiteralPath $candidate).Path
    }

    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        if ([System.IO.Path]::GetExtension($candidate) -notin @('.zip', '.nupkg')) { throw "Artifact '$candidate' is not an extractable zip/nupkg payload." }
        $cleanup = Join-Path ([System.IO.Path]::GetTempPath()) "snapture-sbom-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $cleanup -Force | Out-Null
        Expand-Archive -LiteralPath $candidate -DestinationPath $cleanup -Force
        $payloadCandidate = @(
            $cleanup,
            (Get-ChildItem -LiteralPath $cleanup -Directory -Force | Select-Object -First 1).FullName
        ) | Where-Object { $_ -and (Test-Path -LiteralPath (Join-Path $_ 'Snapture.App.deps.json')) } | Select-Object -First 1
        if ($null -eq $payloadCandidate) { throw "Extracted artifact '$candidate' does not contain Snapture.App.deps.json." }
        return [ordered]@{ Root = $payloadCandidate; Cleanup = $cleanup }
    }
    return [ordered]@{ Root = $candidate; Cleanup = $cleanup }
}

function Invoke-VerifyRuntime {
    param([string]$PackageRuntime, [string]$ExplicitArtifactRoot)

    $resolved = Resolve-ArtifactRoot $PackageRuntime $ExplicitArtifactRoot
    try {
        $manifest = Get-ArtifactManifest $resolved.Root
        $fileMap = Get-ArtifactFileMap $manifest
        Assert-RequiredArtifactFiles $PackageRuntime $fileMap
        $packages = Get-ResolvedPackages $PackageRuntime $resolved.Root
        $dotnetVersions = Get-DotNetRuntimeVersions
        $floorResults = Invoke-SecurityFloorChecks $PackageRuntime $packages $fileMap $dotnetVersions
        $failures = @($floorResults | Where-Object Status -eq 'fail')

        $runtimeOutput = Join-Path $OutputRoot $PackageRuntime
        New-Item -ItemType Directory -Path $runtimeOutput -Force | Out-Null
        $manifestName = "Snapture-$PackageRuntime-artifact-manifest.json"
        $floorName = "Snapture-$PackageRuntime-security-floors.json"
        $licenseName = "Snapture-$PackageRuntime-license-inventory.json"
        $sbomName = "Snapture-v$(Get-ProjectVersion)-$PackageRuntime.cdx.json"
        $manifestPath = Join-Path $runtimeOutput $manifestName
        $floorPath = Join-Path $runtimeOutput $floorName
        $licensePath = Join-Path $runtimeOutput $licenseName
        $sbomPath = Join-Path $runtimeOutput $sbomName

        $manifestForOutput = [ordered]@{
            SchemaVersion = $manifest.SchemaVersion
            Runtime = $PackageRuntime
            ArtifactRoot = $manifest.ArtifactRoot
            ArtifactSha256 = $manifest.ArtifactSha256
            Files = $manifest.Files
        }
        ConvertTo-JsonFile $manifestPath $manifestForOutput
        $floorReport = [ordered]@{
            SchemaVersion = 1
            Runtime = $PackageRuntime
            ArtifactSha256 = $manifest.ArtifactSha256
            Status = if ($failures.Count -eq 0) { 'pass' } else { 'fail' }
            Checks = $floorResults
        }
        ConvertTo-JsonFile $floorPath $floorReport

        $licenseRows = @($packages | Sort-Object Name | ForEach-Object {
            $license = Get-LicenseId $_.Name
            [ordered]@{
                Name = $_.Name
                Version = $_.Version
                License = $license
                Evidence = if ($license -eq 'NOASSERTION') { 'No local SPDX mapping; review before distribution.' } else { 'Local package license inventory.' }
            }
        })
        ConvertTo-JsonFile $licensePath ([ordered]@{
            SchemaVersion = 1
            Runtime = $PackageRuntime
            ArtifactSha256 = $manifest.ArtifactSha256
            UnknownLicenseCount = @($licenseRows | Where-Object License -eq 'NOASSERTION').Count
            Packages = $licenseRows
        })

        $sbom = New-Sbom $PackageRuntime (Get-ProjectVersion) $manifest $packages $fileMap $dotnetVersions $manifestName $floorName $licenseName
        ConvertTo-JsonFile $sbomPath $sbom
        Test-ArtifactManifest $resolved.Root $manifest | Out-Null

        if ($failures.Count -gt 0) {
            throw "Security floor verification failed for ${PackageRuntime}: $((@($failures | ForEach-Object { "$($_.Label) actual $($_.Actual), minimum $($_.Minimum)" })) -join '; ')"
        }
        Write-Host "==> Verified $PackageRuntime release payload" -ForegroundColor Green
        Write-Host "    Artifact SHA256: $($manifest.ArtifactSha256)" -ForegroundColor Green
        Write-Host "    SBOM: $sbomPath" -ForegroundColor Green
        Write-Host "    License inventory: $licensePath" -ForegroundColor Green
    }
    finally {
        if ($resolved.Cleanup -and (Test-Path -LiteralPath $resolved.Cleanup)) { [System.IO.Directory]::Delete($resolved.Cleanup, $true) }
    }
}

function Assert-SelfTest {
    param([bool]$Condition, [string]$Message)

    if (-not $Condition) { throw "SBOM verifier self-test failed: $Message" }
}

function Invoke-SelfTest {
    $fixture = Join-Path ([System.IO.Path]::GetTempPath()) "snapture-sbom-selftest-$([guid]::NewGuid().ToString('N'))"
    try {
        New-Item -ItemType Directory -Path $fixture -Force | Out-Null
        $required = @(
            'Snapture.App.exe', 'Snapture.App.dll', 'Snapture.App.deps.json', 'Snapture.App.runtimeconfig.json',
            'e_sqlite3.dll', 'Magick.NET-Q8-x64.dll', 'Magick.Native-Q8-x64.dll', 'Microsoft.Data.Sqlite.dll',
            'Microsoft.ML.OnnxRuntime.dll', 'onnxruntime.dll', 'onnxruntime_providers_shared.dll', 'RapidOcrNet.dll',
            'SkiaSharp.dll', 'SkiaSharp.Views.WPF.dll', 'libSkiaSharp.dll', 'models\fixture.onnx'
        )
        foreach ($name in $required) {
            $path = Join-Path $fixture $name
            New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
            [System.IO.File]::WriteAllText($path, "fixture:$name", [System.Text.UTF8Encoding]::new($false))
        }
        $manifest = Get-ArtifactManifest $fixture
        $map = Get-ArtifactFileMap $manifest
        Assert-RequiredArtifactFiles 'win-x64' $map

        $packageValues = @(
            @{ Name = 'SQLite'; Version = '3.53.4'; Source = 'self-test' },
            @{ Name = 'Magick.NET-Q8-x64'; Version = '14.15.0'; Source = 'self-test' },
            @{ Name = 'Microsoft.WindowsAppSDK.AI'; Version = '1.8.76'; Source = 'self-test' },
            @{ Name = 'Microsoft.WindowsAppSDK.Foundation'; Version = '1.8.260505001'; Source = 'self-test' },
            @{ Name = 'Microsoft.WindowsAppSDK.Runtime'; Version = '1.8.260508005'; Source = 'self-test' },
            @{ Name = 'Microsoft.ML.OnnxRuntime'; Version = '1.27.1'; Source = 'self-test' },
            @{ Name = 'Microsoft.ML.OnnxRuntime.Managed'; Version = '1.27.1'; Source = 'self-test' },
            @{ Name = 'SkiaSharp'; Version = '3.119.2'; Source = 'self-test' },
            @{ Name = 'SkiaSharp.NativeAssets.Win32'; Version = '3.119.2'; Source = 'self-test' }
        )
        $runtimeVersions = @(
            @{ Name = 'Microsoft.NETCore.App'; Version = '10.0.10' },
            @{ Name = 'Microsoft.WindowsDesktop.App'; Version = '10.0.10' },
            @{ Name = 'Microsoft.AspNetCore.App'; Version = '10.0.10' }
        )
        $passing = Invoke-SecurityFloorChecks 'win-x64' $packageValues $map $runtimeVersions
        Assert-SelfTest (@($passing | Where-Object Status -eq 'fail').Count -eq 0) 'current floors should pass'

        $stalePackages = @($packageValues | ForEach-Object { $_.Clone() })
        ($stalePackages | Where-Object Name -eq 'SQLite').Version = '3.52.0'
        ($stalePackages | Where-Object Name -eq 'Magick.NET-Q8-x64').Version = '14.14.0'
        $staleRuntimeVersions = @(
            @{ Name = 'Microsoft.NETCore.App'; Version = '10.0.9' },
            @{ Name = 'Microsoft.WindowsDesktop.App'; Version = '10.0.9' },
            @{ Name = 'Microsoft.AspNetCore.App'; Version = '10.0.9' }
        )
        $stale = Invoke-SecurityFloorChecks 'win-x64' $stalePackages $map $staleRuntimeVersions
        $staleIds = @($stale | Where-Object Status -eq 'fail' | ForEach-Object Id)
        Assert-SelfTest ($staleIds -contains 'sqlite-package') 'stale SQLite floor must fail'
        Assert-SelfTest ($staleIds -contains 'magick-net-package') 'stale Magick.NET floor must fail'
        Assert-SelfTest ($staleIds -contains 'dotnet-core-runtime') 'stale .NET floor must fail'

        $manifestForTest = [ordered]@{ ArtifactSha256 = $manifest.ArtifactSha256; Files = $manifest.Files }
        Test-ArtifactManifest $fixture $manifestForTest | Out-Null
        [System.IO.File]::AppendAllText((Join-Path $fixture 'e_sqlite3.dll'), 'tampered', [System.Text.UTF8Encoding]::new($false))
        $tamperDetected = $false
        try { Test-ArtifactManifest $fixture $manifestForTest | Out-Null }
        catch { $tamperDetected = $true }
        Assert-SelfTest $tamperDetected 'artifact tampering must invalidate the manifest'
        Write-Host '==> SBOM verifier self-test passed (pass, stale-floor, and tamper cases).' -ForegroundColor Green
    }
    finally {
        if (Test-Path -LiteralPath $fixture) { [System.IO.Directory]::Delete($fixture, $true) }
    }
}

if ($SelfTest) {
    Invoke-SelfTest
    exit 0
}

if ($ArtifactRoot -and $Runtime.Count -ne 1) { throw '-ArtifactRoot can only be used with one -Runtime value.' }
foreach ($packageRuntime in $Runtime) {
    Invoke-VerifyRuntime $packageRuntime $ArtifactRoot
}
