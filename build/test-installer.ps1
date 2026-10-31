<#
  Installs BulkPrintManager_Setup.exe silently on the (clean) build machine and checks it:
    - installer exit code, installed files, Start menu shortcut
    - "Installed apps" publisher = Harish Vaishnav
    - .exe file properties (company, copyright)
    - right-click menu registration
    - runs BulkPrintManager.exe --selftest against the sample documents
      (opens, previews and prints every format to "Microsoft Print to PDF")
    - uninstalls and checks everything installed is removed
  Results are written to build\test-results.
#>
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$results = Join-Path $repo 'build\test-results'
New-Item -ItemType Directory -Force -Path $results | Out-Null
$log = Join-Path $results 'installer-test.txt'
$failures = 0

function Note([string]$text) { Write-Host $text; Add-Content -Path $log -Value $text }
function Check([bool]$ok, [string]$what) {
    if ($ok) { Note "PASS  $what" } else { Note "FAIL  $what"; Write-Host "::error::FAIL $what"; $script:failures++ }
}

$setup = Join-Path $repo 'build\installer\BulkPrintManager_Setup.exe'
Check (Test-Path $setup) 'Installer file exists'
$setupInfo = (Get-Item $setup).VersionInfo
Note ("Installer size: {0:N1} MB, Company '{1}', Copyright '{2}'" -f ((Get-Item $setup).Length / 1MB), $setupInfo.CompanyName, $setupInfo.LegalCopyright)
Check ($setupInfo.CompanyName -eq 'Harish Vaishnav') 'Installer file Company is Harish Vaishnav'

# --- Install --------------------------------------------------------------------------------
$p = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/ALLUSERS',
    '/TASKS="contextmenu,desktopicon"', "/LOG=`"$results\install.log`"") -Wait -PassThru
Check ($p.ExitCode -eq 0) "Silent install finished (exit code $($p.ExitCode))"

$appDir = Join-Path $env:ProgramFiles 'BulkPrint Manager'
$app = Join-Path $appDir 'BulkPrintManager.exe'
Check (Test-Path $app) "Program installed to $appDir"
Check (Test-Path (Join-Path $appDir 'THIRD-PARTY-NOTICES.txt')) 'License notices installed'
$hasEngine = Test-Path (Join-Path $appDir 'engine\program\soffice.exe')
Note "Office engine installed: $hasEngine"
Check (Test-Path (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\BulkPrint Manager.lnk')) 'Start menu shortcut created'

$uninstallKey = Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' |
    ForEach-Object { Get-ItemProperty $_.PSPath } |
    Where-Object { $_.DisplayName -like 'BulkPrint Manager*' } | Select-Object -First 1
Check ($null -ne $uninstallKey) 'Listed in Windows Installed apps'
if ($uninstallKey) {
    Note "Installed apps entry: '$($uninstallKey.DisplayName)' version $($uninstallKey.DisplayVersion), publisher '$($uninstallKey.Publisher)'"
    Check ($uninstallKey.Publisher -eq 'Harish Vaishnav') 'Installed apps publisher is Harish Vaishnav'
}

$vi = (Get-Item $app).VersionInfo
Note "EXE properties: Product '$($vi.ProductName)', Company '$($vi.CompanyName)', Description '$($vi.FileDescription)', Copyright '$($vi.LegalCopyright)'"
Check ($vi.CompanyName -eq 'Harish Vaishnav') 'EXE Company is Harish Vaishnav'
Check ($vi.LegalCopyright -like '*Harish Vaishnav*') 'EXE Copyright names Harish Vaishnav'

Check (Test-Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\SystemFileAssociations\.pdf\shell\BulkPrintManager\command') 'Right-click menu registered for PDF'
Check (Test-Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\Directory\shell\BulkPrintManager\command') 'Right-click menu registered for folders'

# --- Make sure a print-to-PDF printer exists for the print tests ----------------------------
if (-not (Get-Printer -Name 'Microsoft Print to PDF' -ErrorAction SilentlyContinue)) {
    try {
        Enable-WindowsOptionalFeature -Online -FeatureName 'Printing-PrintToPDFServices-Features' -All -NoRestart | Out-Null
    } catch {
        Note "NOTE  Could not enable Microsoft Print to PDF: $($_.Exception.Message)"
    }
}
Note ('Printers: ' + ((Get-Printer -ErrorAction SilentlyContinue | ForEach-Object Name) -join '; '))

# --- Samples, including legacy DOC/XLS/PPT made with the bundled engine ----------------------
$samples = Join-Path $results 'samples'
New-Item -ItemType Directory -Force -Path $samples | Out-Null
Copy-Item (Join-Path $repo 'tests\samples\*') $samples -Force
if ($hasEngine) {
    $soffice = Join-Path $appDir 'engine\program\soffice.exe'
    foreach ($pair in @(@('sample-letter.docx', 'doc'), @('sample-budget.xlsx', 'xls'), @('sample-slides.pptx', 'ppt'))) {
        $src = Join-Path $samples $pair[0]
        $proc = Start-Process -FilePath $soffice -ArgumentList @('--headless', '--norestore', '--convert-to', $pair[1], '--outdir', "`"$samples`"", "`"$src`"") -Wait -PassThru
        Start-Sleep -Seconds 2
        $made = Join-Path $samples ([IO.Path]::ChangeExtension($pair[0], $pair[1]))
        Check (Test-Path $made) "Created legacy sample $(Split-Path $made -Leaf)"
    }
    Get-Process soffice* -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

# --- Run the program's own self-test --------------------------------------------------------
$selftest = Join-Path $results 'selftest'
$p = Start-Process -FilePath $app -ArgumentList @('--selftest', "`"$samples`"", "`"$selftest`"") -Wait -PassThru
$resultsFile = Join-Path $selftest 'results.txt'
if (Test-Path $resultsFile) {
    Get-Content $resultsFile | ForEach-Object {
        Note "  $_"
        if ($_ -like 'FAIL*') { Write-Host "::error::Self-test $_" }
    }
}
Check ($p.ExitCode -eq 0) "Program self-test passed (exit code $($p.ExitCode))"

# --- Uninstall ------------------------------------------------------------------------------
if ($uninstallKey) {
    $uninstaller = ($uninstallKey.UninstallString -replace '"', '')
    $p = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru
    Start-Sleep -Seconds 3
    Check ($p.ExitCode -eq 0) 'Silent uninstall finished'
    Check (-not (Test-Path $app)) 'Program files removed by uninstall'
    Check (-not (Test-Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\SystemFileAssociations\.pdf\shell\BulkPrintManager')) 'Right-click menu removed by uninstall'
    Check (-not (Test-Path (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\BulkPrint Manager.lnk'))) 'Start menu shortcut removed'
}

Note ''
Note "Installer test finished with $failures failure(s)."
if ($failures -gt 0) { exit 1 }
exit 0
