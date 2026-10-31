<#
  Downloads the official LibreOffice Windows x64 installer and extracts it (administrative
  install, nothing is installed on the build machine) into the BulkPrint Manager "engine" folder.

  Usage:  ./build/fetch-libreoffice.ps1 -Destination build/publish/engine [-Version 25.2.6]
  Leave -Version empty to use the current "still" (most mature) release from the stable channel.
#>
param(
    [Parameter(Mandatory = $true)][string]$Destination,
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$base = 'https://download.documentfoundation.org/libreoffice/stable/'

if ([string]::IsNullOrWhiteSpace($Version)) {
    $html = (Invoke-WebRequest -Uri $base -UseBasicParsing).Content
    $versions = [regex]::Matches($html, 'href="(\d+\.\d+\.\d+)/"') |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object { [version]$_ } -Unique
    if (-not $versions) { throw "Could not read the list of LibreOffice versions from $base" }
    # The stable channel lists "still" (older, most tested) and "fresh". Use "still".
    $Version = @($versions)[0]
}
Write-Host "Bundling LibreOffice $Version"

$work = Join-Path $env:RUNNER_TEMP 'libreoffice'
if (-not $env:RUNNER_TEMP) { $work = Join-Path ([IO.Path]::GetTempPath()) 'libreoffice' }
New-Item -ItemType Directory -Force -Path $work | Out-Null
$msiName = "LibreOffice_${Version}_Win_x86-64.msi"
$msi = Join-Path $work $msiName
$url = "$base$Version/win/x86_64/$msiName"

Write-Host "Downloading $url"
Invoke-WebRequest -Uri $url -OutFile $msi -UseBasicParsing

# Verify the download when the mirror system publishes a checksum.
$actual = (Get-FileHash $msi -Algorithm SHA256).Hash.ToLowerInvariant()
$expected = $null
try {
    $resp = Invoke-WebRequest -Uri "$url.sha256" -UseBasicParsing
    $text = if ($resp.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($resp.Content) } else { [string]$resp.Content }
    $expected = ($text.Trim() -split '\s+')[0].ToLowerInvariant()
} catch {
    Write-Warning "Checksum file not available ($($_.Exception.Message)); continuing."
}
if ($expected -match '^[0-9a-f]{64}$') {
    if ($expected -ne $actual) { throw "Checksum mismatch for $msiName (expected $expected, got $actual)" }
    Write-Host "SHA-256 verified: $actual"
} else {
    Write-Warning "No usable checksum published. SHA-256 of the download: $actual"
}

$admin = Join-Path $work 'admin'
if (Test-Path $admin) { Remove-Item $admin -Recurse -Force }
Write-Host 'Extracting (administrative install)...'
$p = Start-Process msiexec.exe -ArgumentList @('/a', "`"$msi`"", '/qn', "TARGETDIR=`"$admin`"") -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "msiexec /a failed with exit code $($p.ExitCode)" }

$soffice = Get-ChildItem -Path $admin -Recurse -Filter 'soffice.exe' | Select-Object -First 1
if (-not $soffice) { throw 'soffice.exe was not found after extracting LibreOffice.' }
$root = $soffice.Directory.Parent.FullName
Write-Host "Engine root: $root"

if (Test-Path $Destination) { Remove-Item $Destination -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
robocopy $root $Destination /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying the engine failed (robocopy exit code $LASTEXITCODE)" }
$global:LASTEXITCODE = 0

if (-not (Test-Path (Join-Path $Destination 'program\soffice.exe'))) { throw 'engine\program\soffice.exe is missing after copy.' }
Set-Content -Path (Join-Path $Destination 'BUNDLED-VERSION.txt') -Value @(
    "LibreOffice $Version (unmodified, Windows x86-64)",
    "Downloaded from $url",
    "Source code: https://www.libreoffice.org/download/source-code/",
    "License: MPL-2.0 and others, see the license files in this folder."
)

$sizeMb = [math]::Round(((Get-ChildItem $Destination -Recurse | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "Office engine ready: $Destination ($sizeMb MB)"
