[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$Clean,
    [switch]$Publish,
    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

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

Write-Host "==> Done." -ForegroundColor Green
