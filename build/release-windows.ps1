<#
.SYNOPSIS
  Builds the Windows release of PurgeCAD: installer with one-click updates (Velopack), portable zip, update package.

.DESCRIPTION
  The version comes from <Version> in Directory.Build.props. Output in artifacts\releases:
    PurgeCAD-win-Setup.exe      the installer; the SAME name in every version, so the download link never changes:
                                https://github.com/SyntelabHQ/purge-cad/releases/latest/download/PurgeCAD-win-Setup.exe
    PurgeCAD-win-Portable.zip   portable copy (also updates itself)
    PurgeCAD-<v>-full.nupkg     update package (+ delta when a previous release exists), releases.win.json
  plus a versioned copy kept for the archive: artifacts\PurgeCAD-<v>-win-Setup.exe

  With -Upload the files go to a GitHub release v<version> (created and published), and the installed copies of
  PurgeCAD see the update on their next start. Needs a GitHub token with "contents: write" on the repository,
  in -Token or the GITHUB_TOKEN environment variable.

.EXAMPLE
  .\build\release-windows.ps1
  .\build\release-windows.ps1 -Upload -Notes docs\notes\1.1.0.md
  .\build\release-windows.ps1 -SignParams '/a /fd sha256 /tr http://timestamp.digicert.com /td sha256'
#>
param(
    [switch]$Upload,
    [string]$Token = $env:GITHUB_TOKEN,
    # Markdown file with what changed (shown on the release page)
    [string]$Notes = "",
    # signtool.exe parameters, to sign the app and the installer with a code-signing certificate
    [string]$SignParams = "",
    [string]$Repository = "https://github.com/SyntelabHQ/purge-cad"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
if (-not $version) { throw "No <Version> in Directory.Build.props" }
Write-Host "PurgeCAD $version" -ForegroundColor Cyan

$artifacts = Join-Path $root "artifacts"
$publish = Join-Path $artifacts "publish\win-x64"
$releases = Join-Path $artifacts "releases"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
New-Item -ItemType Directory -Force -Path $releases | Out-Null

dotnet tool restore
if ($LASTEXITCODE) { throw "dotnet tool restore failed" }

dotnet test tests\PurgeCad.Core.Tests -c Release
if ($LASTEXITCODE) { throw "Tests failed" }

dotnet publish src\PurgeCad.App -c Release -r win-x64 --self-contained -o $publish
if ($LASTEXITCODE) { throw "Publish failed" }
if (-not (Test-Path (Join-Path $publish "acad-plugin\net462\PurgeCad.AutoCad.Plugin.dll"))) { throw "AutoCAD plugin missing from the build" }

if ($Upload) {
    if (-not $Token) { throw "Upload needs a GitHub token (-Token or GITHUB_TOKEN)" }
    # the previous release, so the update can be a small delta instead of the full package
    dotnet vpk download github --repoUrl $Repository --token $Token -o $releases
}

$pack = @("vpk", "pack", "--packId", "PurgeCAD", "--packVersion", $version, "--packDir", $publish,
          "--mainExe", "PurgeCAD.exe", "--packTitle", "PurgeCAD", "--packAuthors", "Syntelab",
          "--icon", "src\PurgeCad.App\Assets\purge-cad.ico", "--outputDir", $releases)
if ($Notes) { $pack += @("--releaseNotes", $Notes) }
if ($SignParams) { $pack += @("--signParams", $SignParams) }
dotnet @pack
if ($LASTEXITCODE) { throw "vpk pack failed" }

# Versioned copy for the archive; the fixed name (PurgeCAD-win-Setup.exe) is what users download.
Copy-Item (Join-Path $releases "PurgeCAD-win-Setup.exe") (Join-Path $artifacts "PurgeCAD-$version-win-Setup.exe") -Force

if ($Upload) {
    dotnet vpk upload github --repoUrl $Repository --token $Token -o $releases `
        --tag "v$version" --releaseName "PurgeCAD $version" --merge --publish
    if ($LASTEXITCODE) { throw "Upload failed" }
    Write-Host "Published: $Repository/releases/tag/v$version" -ForegroundColor Green
}
Write-Host "Done: $releases" -ForegroundColor Green
