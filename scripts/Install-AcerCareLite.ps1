#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Installs AcerCareLite (and its elevated read-only helper) into C:\Program Files\AcerCareLite, where only
  administrators can modify it. Run from an ELEVATED PowerShell:

    .\scripts\Install-AcerCareLite.ps1              # install / update
    .\scripts\Install-AcerCareLite.ps1 -Uninstall   # remove
    .\scripts\Install-AcerCareLite.ps1 -EnableWrite # EXPERIMENTAL: build with the ability to CHANGE the battery health mode

  This is a development install: the binaries are not code-signed. It never touches anything outside
  "$env:ProgramFiles\AcerCareLite" (plus a temporary staging folder it deletes afterwards).
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$EnableWrite
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$AppName  = 'AcerCareLite'
$Target   = Join-Path $env:ProgramFiles $AppName
$RepoRoot = Split-Path -Parent $PSScriptRoot

function Assert-NotRunning {
    if (Get-Process -Name $AppName -ErrorAction SilentlyContinue) {
        throw "$AppName is running. Close it (including the tray icon) and run this script again."
    }
}

if ($Uninstall) {
    Assert-NotRunning
    if (Test-Path -LiteralPath $Target) {
        Remove-Item -LiteralPath $Target -Recurse -Force
        Write-Host "Removed $Target"
    } else {
        Write-Host "Nothing to remove: $Target does not exist."
    }
    return
}

$WriteFlag = if ($EnableWrite) { 'true' } else { 'false' }
if ($EnableWrite) {
    Write-Warning 'EXPERIMENTAL: this build can CHANGE the Acer battery health mode in the firmware (health bit only; never calibration).'
    Write-Warning 'Every attempt asks for confirmation and administrator approval, is verified by readback, and is logged to %LocalAppData%\AcerCareLite\acer-write-audit.log.'
    $answer = Read-Host 'Type ENABLE to continue'
    if ($answer -cne 'ENABLE') { throw 'Cancelled. Nothing was installed.' }
}

$Staging = Join-Path ([System.IO.Path]::GetTempPath()) ("$AppName-stage-" + [guid]::NewGuid().ToString('N'))
try {
    Write-Host "Publishing (Release, win-x64, framework-dependent) to $Staging ..."
    dotnet publish (Join-Path $RepoRoot 'src\AcerCareLite.App\AcerCareLite.App.csproj') -c Release -r win-x64 --self-contained false -p:AcerWriteEnabled=$WriteFlag -o $Staging
    if ($LASTEXITCODE -ne 0) { throw 'Publishing the app failed.' }
    dotnet publish (Join-Path $RepoRoot 'src\AcerCareLite.AcerHelper\AcerCareLite.AcerHelper.csproj') -c Release -r win-x64 --self-contained false -p:AcerWriteEnabled=$WriteFlag -o $Staging
    if ($LASTEXITCODE -ne 0) { throw 'Publishing the helper failed.' }

    foreach ($required in 'AcerCareLite.exe', 'AcerCareLite.AcerHelper.exe') {
        if (-not (Test-Path -LiteralPath (Join-Path $Staging $required))) { throw "Staging is missing $required." }
    }

    Assert-NotRunning
    New-Item -ItemType Directory -Force -Path $Target | Out-Null

    # Mirror the staging folder into the install folder (this folder is ours alone).
    robocopy $Staging $Target /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed (robocopy exit code $LASTEXITCODE)." }

    # Reset every item to the permissions inherited from Program Files (SYSTEM, Administrators and TrustedInstaller
    # full control; Users read/execute), and make Administrators the owner.
    icacls $Target /reset /T /C | Out-Null
    icacls $Target /setowner '*S-1-5-32-544' /T /C | Out-Null

    Write-Host ''
    Write-Host "Installed to $Target"
    icacls (Join-Path $Target 'AcerCareLite.AcerHelper.exe')
    Write-Host ''
    Write-Host ("Write support in this install: " + $(if ($EnableWrite) { 'ENABLED (experimental)' } else { 'not included' }))
    Write-Host "Start it normally (not as administrator): & '$Target\AcerCareLite.exe'"
    Write-Host "The Battery page should now report: Helper location: protected."
}
finally {
    if (Test-Path -LiteralPath $Staging) { Remove-Item -LiteralPath $Staging -Recurse -Force }
}
