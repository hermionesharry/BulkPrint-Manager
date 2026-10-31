<#
  Signs a file with the code-signing certificate stored in the SIGNING_CERT_BASE64 and
  SIGNING_CERT_PASSWORD environment variables (set from GitHub repository secrets).
  Signing removes most Windows SmartScreen warnings. It is optional.
#>
param([Parameter(Mandatory = $true)][string]$File)
$ErrorActionPreference = 'Stop'
$pfx = Join-Path $env:RUNNER_TEMP 'signing-cert.pfx'
try {
    [IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:SIGNING_CERT_BASE64))
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe |
        Where-Object { $_.FullName -like '*\x64\*' } | Sort-Object FullName | Select-Object -Last 1
    if (-not $signtool) { throw 'signtool.exe not found' }
    & $signtool.FullName sign /f $pfx /p $env:SIGNING_CERT_PASSWORD /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /d "BulkPrint Manager" $File
    if ($LASTEXITCODE -ne 0) { throw "Signing failed with exit code $LASTEXITCODE" }
} finally {
    Remove-Item $pfx -ErrorAction SilentlyContinue
}
