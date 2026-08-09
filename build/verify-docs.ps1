[CmdletBinding()]
param(
    [string]$RootPath,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RootPath)) {
    $RootPath = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}
$script:Root = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $RootPath).Path)

function Get-RepositoryText {
    param(
        [string]$RepositoryRoot,
        [string]$RelativePath,
        [System.Collections.Generic.List[string]]$Issues
    )

    $path = Join-Path $RepositoryRoot ($RelativePath -replace '/', '\')
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $null = $Issues.Add("missing required file '$RelativePath'")
        return ''
    }

    try {
        return [System.IO.File]::ReadAllText($path)
    }
    catch {
        $null = $Issues.Add("could not read '$RelativePath': $($_.Exception.Message)")
        return ''
    }
}

function Add-MissingTextIssue {
    param(
        [System.Collections.Generic.List[string]]$Issues,
        [string]$Label,
        [string]$Text,
        [string]$Pattern
    )

    if ([string]::IsNullOrWhiteSpace($Text) -or $Text -notmatch $Pattern) {
        $null = $Issues.Add("$Label is missing '$Pattern'")
    }
}

function Invoke-DocumentationCheck {
    param([string]$RepositoryRoot)

    $issues = [System.Collections.Generic.List[string]]::new()
    $texts = @{}
    $requiredFiles = @(
        'README.md',
        'CHANGELOG.md',
        'docs/INSTALL.md',
        'docs/PLUGINS.md',
        'docs/ARCHITECTURE.md',
        'docs/PRIVACY.md',
        'src/Snapture.App/Snapture.App.csproj',
        'src/Snapture.Capture/Snapture.Capture.csproj',
        'src/Snapture.Plugin.Abstractions/Snapture.Plugin.Abstractions.csproj',
        'src/Snapture.App/Services/CliCommandLine.cs',
        'src/Snapture.App/Services/OutboundDataFlowAudit.cs',
        'build/build.ps1',
        'packaging/msix/Package.appxmanifest',
        'packaging/msix/Snapture.appinstaller.template.xml',
        'packaging/msi/Snapture.wxs',
        'packaging/portable/Snapture.ini',
        'packaging/scoop/snapture.json'
    )

    foreach ($relativePath in $requiredFiles) {
        $texts[$relativePath] = Get-RepositoryText $RepositoryRoot $relativePath $issues
    }

    $manifestRoot = Join-Path $RepositoryRoot 'manifests/SysAdminDoc/Snapture'
    $projectVersion = $null
    $appPackages = @{}
    $appProjectText = $texts['src/Snapture.App/Snapture.App.csproj']
    if (-not [string]::IsNullOrWhiteSpace($appProjectText)) {
        try {
            [xml]$appProject = $appProjectText
            $projectVersion = @($appProject.Project.PropertyGroup | ForEach-Object { [string]$_.Version } |
                    Where-Object { -not [string]::IsNullOrWhiteSpace($_) })[0]
            foreach ($reference in @($appProject.Project.ItemGroup.PackageReference)) {
                $name = [string]$reference.Include
                $version = [string]$reference.Version
                if ([string]::IsNullOrWhiteSpace($version) -and $null -ne $reference.Version) {
                    $version = [string]$reference.Version.'#text'
                }
                if (-not [string]::IsNullOrWhiteSpace($name) -and -not [string]::IsNullOrWhiteSpace($version)) {
                    $appPackages[$name] = $version
                }
            }
        }
        catch {
            $null = $issues.Add("could not parse src/Snapture.App/Snapture.App.csproj: $($_.Exception.Message)")
        }
    }

    if ([string]::IsNullOrWhiteSpace($projectVersion)) {
        $null = $issues.Add('the application project has no <Version> value')
        $projectVersion = 'unknown'
    }

    $assemblyVersion = if ($projectVersion -match '^\d+\.\d+\.\d+$') {
        "$projectVersion.0"
    }
    else {
        $projectVersion
    }

    foreach ($project in @(
            'src/Snapture.App/Snapture.App.csproj',
            'src/Snapture.Capture/Snapture.Capture.csproj',
            'src/Snapture.Plugin.Abstractions/Snapture.Plugin.Abstractions.csproj')) {
        $projectText = $texts[$project]
        Add-MissingTextIssue $issues "$project version" $projectText "<Version>$([regex]::Escape($projectVersion))</Version>"
        Add-MissingTextIssue $issues "$project assembly version" $projectText "<AssemblyVersion>$([regex]::Escape($assemblyVersion))</AssemblyVersion>"
        Add-MissingTextIssue $issues "$project file version" $projectText "<FileVersion>$([regex]::Escape($assemblyVersion))</FileVersion>"
    }

    $readme = $texts['README.md']
    $changelog = $texts['CHANGELOG.md']
    $install = $texts['docs/INSTALL.md']
    $plugins = $texts['docs/PLUGINS.md']
    $architecture = $texts['docs/ARCHITECTURE.md']
    $privacy = $texts['docs/PRIVACY.md']
    $escapedVersion = [regex]::Escape($projectVersion)

    Add-MissingTextIssue $issues 'README version badge' $readme "shields\.io/badge/version-$escapedVersion-"
    Add-MissingTextIssue $issues 'README release section' $readme "(?im)^## What ships in v$escapedVersion\s*$"
    Add-MissingTextIssue $issues 'CHANGELOG current release' $changelog "(?im)^## \[v$escapedVersion\]"
    Add-MissingTextIssue $issues 'README release download wording' $readme 'latest tagged release'

    $staleClaims = [ordered]@{
        'README recording landing claim' = 'recording is landing on `main`'
        'README first-tag claim' = 'once the first tag is cut'
        'INSTALL future portable claim' = 'future release.{0,80}--portable'
        'INSTALL planned ARM64 claim' = 'ARM64 builds are planned'
        'PLUGINS missing credential-store claim' = 'does not provide a credential store'
        'PLUGINS future-adapter claim' = 'future external adapters'
    }
    foreach ($staleClaim in $staleClaims.GetEnumerator()) {
        $claimText = "$readme`n$install`n$plugins"
        if ($claimText -match $staleClaim.Value) {
            $null = $issues.Add("$($staleClaim.Key) is still present")
        }
    }

    Add-MissingTextIssue $issues 'INSTALL portable-mode claim' $install '--portable'
    Add-MissingTextIssue $issues 'INSTALL portable data-root claim' $install 'SnaptureData'
    Add-MissingTextIssue $issues 'INSTALL ARM64 claim' $install 'win-arm64'
    Add-MissingTextIssue $issues 'INSTALL release architecture claim' $install 'x64 and ARM64'
    Add-MissingTextIssue $issues 'PLUGIN secret-store contract' $plugins 'IPluginSecretStore'
    Add-MissingTextIssue $issues 'PLUGIN DPAPI guidance' $plugins 'DPAPI'
    Add-MissingTextIssue $issues 'PLUGIN current-user storage guidance' $plugins 'current-user'
    Add-MissingTextIssue $issues 'ARCHITECTURE target framework' $architecture 'net10\.0-windows10\.0\.22621\.0'
    Add-MissingTextIssue $issues 'ARCHITECTURE minimum platform' $architecture '10\.0\.17763\.0'

    $architecturePackages = @(
        'SkiaSharp.Views.WPF',
        'CommunityToolkit.Mvvm',
        'Microsoft.Data.Sqlite',
        'NAudio',
        'Serilog',
        'Hardcodet.NotifyIcon.Wpf'
    )
    foreach ($packageName in $architecturePackages) {
        if (-not $appPackages.ContainsKey($packageName)) {
            $null = $issues.Add("architecture package '$packageName' is not declared by the application project")
            continue
        }

        $packageVersion = [regex]::Escape([string]$appPackages[$packageName])
        $rowPattern = "(?im)^\|\s*$([regex]::Escape($packageName))\s*\|\s*$packageVersion\s*\|"
        Add-MissingTextIssue $issues "ARCHITECTURE package row '$packageName'" $architecture $rowPattern
    }

    $cliSource = $texts['src/Snapture.App/Services/CliCommandLine.cs']
    $usageMatch = [regex]::Match($cliSource, '(?s)public const string Usage\s*=\s*(?<usage>.*?);\s*\n')
    if (-not $usageMatch.Success) {
        $null = $issues.Add('could not extract CliCommandLine.Usage for flag validation')
    }
    else {
        $usage = $usageMatch.Groups['usage'].Value
        $cliFlags = @([regex]::Matches($usage, '--[a-z-]+') | ForEach-Object { $_.Value } | Sort-Object -Unique)
        foreach ($flag in $cliFlags) {
            Add-MissingTextIssue $issues "README CLI flag '$flag'" $readme ([regex]::Escape($flag))
        }
    }

    $privacyClaims = [ordered]@{
        'MCP listener' = '\bMCP\b'
        'Velopack update feed' = '\bVelopack\b'
        'declarative uploader' = 'Declarative uploader'
        'Nextcloud destination' = '\bNextcloud\b'
        'Immich destination' = '\bImmich\b'
        'plugin dependency download' = 'dependency download'
        'external command boundary' = 'External command'
        'OneOCR sidecar' = '\bOneOCR\b'
        'magnification helper' = '[Mm]agnification'
        'maintained source inventory' = 'OutboundDataFlowAudit'
    }
    foreach ($privacyClaim in $privacyClaims.GetEnumerator()) {
        Add-MissingTextIssue $issues "PRIVACY $($privacyClaim.Key)" $privacy $privacyClaim.Value
    }

    $buildText = $texts['build/build.ps1']
    Add-MissingTextIssue $issues 'build version extraction' $buildText 'Get-ProjectVersion'
    Add-MissingTextIssue $issues 'build MSIX version substitution' $buildText '__PACKAGE_VERSION__'
    Add-MissingTextIssue $issues 'build Velopack version substitution' $buildText '--packVersion'
    Add-MissingTextIssue $issues 'portable marker' $texts['packaging/portable/Snapture.ini'] 'Portable\s*=\s*true'

    $msix = $texts['packaging/msix/Package.appxmanifest']
    $appInstaller = $texts['packaging/msix/Snapture.appinstaller.template.xml']
    $msi = $texts['packaging/msi/Snapture.wxs']
    Add-MissingTextIssue $issues 'MSIX package-version placeholder' $msix 'Version="__PACKAGE_VERSION__"'
    Add-MissingTextIssue $issues 'MSIX architecture placeholder' $msix 'ProcessorArchitecture="__ARCHITECTURE__"'
    Add-MissingTextIssue $issues 'App Installer version placeholder' $appInstaller 'Version="__PACKAGE_VERSION__"'
    Add-MissingTextIssue $issues 'MSI package-version variable' $msi 'Version="\$\(var\.PackageVersion\)"'

    try {
        $scoop = $texts['packaging/scoop/snapture.json'] | ConvertFrom-Json
        if ([string]$scoop.version -ne $projectVersion) {
            $null = $issues.Add("Scoop version '$($scoop.version)' does not match project version '$projectVersion'")
        }
        foreach ($architectureName in @('64bit', 'arm64')) {
            $url = [string]$scoop.architecture.$architectureName.url
            if ($url -notmatch "/v$escapedVersion/") {
                $null = $issues.Add("Scoop $architectureName URL does not reference v$projectVersion")
            }
        }
    }
    catch {
        $null = $issues.Add("could not parse packaging/scoop/snapture.json: $($_.Exception.Message)")
    }

    $currentManifestPath = Join-Path $manifestRoot $projectVersion
    if (-not (Test-Path -LiteralPath $currentManifestPath -PathType Container)) {
        $null = $issues.Add("missing current winget manifest directory '$projectVersion'")
    }
    else {
        $manifestFiles = @(Get-ChildItem -LiteralPath $currentManifestPath -Filter '*.yaml' -File)
        if ($manifestFiles.Count -lt 3) {
            $null = $issues.Add("winget manifest directory '$projectVersion' must contain version, installer, and locale YAML files")
        }
        foreach ($manifestFile in $manifestFiles) {
            $manifestText = [System.IO.File]::ReadAllText($manifestFile.FullName)
            $matches = @([regex]::Matches($manifestText, '(?m)^PackageVersion:\s*(\S+)\s*$') | ForEach-Object { $_.Groups[1].Value })
            if ($matches.Count -eq 0) {
                $null = $issues.Add("winget manifest '$($manifestFile.Name)' has no PackageVersion row")
            }
            elseif ($matches | Where-Object { $_ -ne $projectVersion }) {
                $null = $issues.Add("winget manifest '$($manifestFile.Name)' has a PackageVersion that does not match '$projectVersion'")
            }
            if ($manifestFile.Name -like '*installer.yaml' -and $manifestText -notmatch "/v$escapedVersion/") {
                $null = $issues.Add("winget installer manifest '$($manifestFile.Name)' does not reference v$projectVersion")
            }
        }
    }

    [pscustomobject]@{
        IsValid = $issues.Count -eq 0
        Version = $projectVersion
        Issues = @($issues)
    }
}

function Assert-DocumentationSelfTest {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw "Documentation verifier self-test failed: $Message"
    }
}

function Copy-SelfTestFiles {
    param([string]$DestinationRoot)

    $files = @(
        'README.md',
        'CHANGELOG.md',
        'docs/INSTALL.md',
        'docs/PLUGINS.md',
        'docs/ARCHITECTURE.md',
        'docs/PRIVACY.md',
        'src/Snapture.App/Snapture.App.csproj',
        'src/Snapture.Capture/Snapture.Capture.csproj',
        'src/Snapture.Plugin.Abstractions/Snapture.Plugin.Abstractions.csproj',
        'src/Snapture.App/Services/CliCommandLine.cs',
        'src/Snapture.App/Services/OutboundDataFlowAudit.cs',
        'build/build.ps1',
        'packaging/msix/Package.appxmanifest',
        'packaging/msix/Snapture.appinstaller.template.xml',
        'packaging/msi/Snapture.wxs',
        'packaging/portable/Snapture.ini',
        'packaging/scoop/snapture.json'
    )

    $manifestVersion = Invoke-DocumentationCheck $script:Root | Select-Object -ExpandProperty Version
    $manifestDirectory = Join-Path $script:Root ("manifests/SysAdminDoc/Snapture/$manifestVersion")
    foreach ($sourceRelativePath in $files) {
        $source = Join-Path $script:Root ($sourceRelativePath -replace '/', '\')
        $destination = Join-Path $DestinationRoot ($sourceRelativePath -replace '/', '\')
        [System.IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
        [System.IO.File]::Copy($source, $destination)
    }

    foreach ($manifestFile in @(Get-ChildItem -LiteralPath $manifestDirectory -Filter '*.yaml' -File)) {
        $relative = "manifests/SysAdminDoc/Snapture/$manifestVersion/$($manifestFile.Name)"
        $destination = Join-Path $DestinationRoot ($relative -replace '/', '\')
        [System.IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
        [System.IO.File]::Copy($manifestFile.FullName, $destination)
    }
}

function Invoke-SelfTest {
    $fixture = Join-Path ([System.IO.Path]::GetTempPath()) "snapture-docs-selftest-$([guid]::NewGuid().ToString('N'))"
    try {
        [System.IO.Directory]::CreateDirectory($fixture) | Out-Null
        Copy-SelfTestFiles $fixture

        $beforeMarkdown = @(Get-ChildItem -LiteralPath $fixture -Recurse -Filter '*.md' -File).Count
        $passing = Invoke-DocumentationCheck $fixture
        Assert-DocumentationSelfTest $passing.IsValid "current documentation should pass: $($passing.Issues -join '; ')"
        $afterMarkdown = @(Get-ChildItem -LiteralPath $fixture -Recurse -Filter '*.md' -File).Count
        Assert-DocumentationSelfTest ($beforeMarkdown -eq $afterMarkdown) 'the check must not create Markdown artifacts'

        $mutations = @(
            @{ Name = 'portable data root'; RelativePath = 'docs/INSTALL.md'; Find = 'SnaptureData'; Replace = 'PortableData' },
            @{ Name = 'ARM64 support'; RelativePath = 'docs/INSTALL.md'; Find = 'win-arm64'; Replace = 'win-legacy' },
            @{ Name = 'plugin credential storage'; RelativePath = 'docs/PLUGINS.md'; Find = 'DPAPI'; Replace = 'plaintext storage' },
            @{ Name = 'version badge'; RelativePath = 'README.md'; Find = "version-$($passing.Version)"; Replace = 'version-0.0.0' },
            @{ Name = 'package manifest'; RelativePath = 'packaging/scoop/snapture.json'; Find = '"version": "' + $passing.Version + '"'; Replace = '"version": "0.0.0"' },
            @{ Name = 'architecture package row'; RelativePath = 'docs/ARCHITECTURE.md'; Find = 'SkiaSharp.Views.WPF | 3.119.2'; Replace = 'SkiaSharp.Views.WPF | 2.88.9' },
            @{ Name = 'network boundary'; RelativePath = 'docs/PRIVACY.md'; Find = 'MCP'; Replace = 'local protocol' }
        )

        foreach ($mutation in $mutations) {
            $path = Join-Path $fixture ($mutation.RelativePath -replace '/', '\')
            $original = [System.IO.File]::ReadAllText($path)
            try {
                $mutated = if ($mutation.Name -eq 'network boundary') {
                    [regex]::Replace($original, '(?i)\bmcp\b', [string]$mutation.Replace)
                }
                else {
                    $original.Replace([string]$mutation.Find, [string]$mutation.Replace)
                }
                [System.IO.File]::WriteAllText($path, $mutated, [System.Text.UTF8Encoding]::new($false))
                $failed = Invoke-DocumentationCheck $fixture
                Assert-DocumentationSelfTest (-not $failed.IsValid) "$($mutation.Name) drift must fail"
            }
            finally {
                [System.IO.File]::WriteAllText($path, $original, [System.Text.UTF8Encoding]::new($false))
            }
        }

        $stalePath = Join-Path $fixture 'README.md'
        $originalReadme = [System.IO.File]::ReadAllText($stalePath)
        try {
            [System.IO.File]::WriteAllText($stalePath, "$originalReadme`nRecording is landing on ``main``.", [System.Text.UTF8Encoding]::new($false))
            $stale = Invoke-DocumentationCheck $fixture
            Assert-DocumentationSelfTest (-not $stale.IsValid) 'known stale release wording must fail'
        }
        finally {
            [System.IO.File]::WriteAllText($stalePath, $originalReadme, [System.Text.UTF8Encoding]::new($false))
        }

        Write-Host '==> Documentation verifier self-test passed (current, version, package, architecture, CLI, network, and stale-claim cases).' -ForegroundColor Green
    }
    finally {
        if (Test-Path -LiteralPath $fixture) {
            [System.IO.Directory]::Delete($fixture, $true)
        }
    }
}

if ($SelfTest) {
    Invoke-SelfTest
    exit 0
}

$report = Invoke-DocumentationCheck $script:Root
if (-not $report.IsValid) {
    Write-Error ("Documentation drift check failed for v{0}:`n - {1}" -f $report.Version, ($report.Issues -join "`n - "))
    exit 1
}

Write-Host "==> Documentation drift check passed for v$($report.Version)" -ForegroundColor Green
