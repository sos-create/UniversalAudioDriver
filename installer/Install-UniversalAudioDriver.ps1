[CmdletBinding()]
param(
    [string]$DriverDirectory = (Join-Path $PSScriptRoot "Drivers"),
    [string]$ReportPath = (Join-Path $PSScriptRoot "audio-install-report.json")
)

$ErrorActionPreference = "Stop"
$Application = Join-Path $PSScriptRoot "UniversalAudioDriver.exe"

if (-not (Test-Path -LiteralPath $Application -PathType Leaf)) {
    throw "UniversalAudioDriver.exe was not found next to this installer: $Application"
}

New-Item -ItemType Directory -Force -Path $DriverDirectory | Out-Null
$ArgumentLine = '--install --driver-dir "{0}" --report "{1}"' -f $DriverDirectory.Replace('"', '""'), $ReportPath.Replace('"', '""')

$Identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$Principal = [Security.Principal.WindowsPrincipal]::new($Identity)
$IsAdministrator = $Principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $IsAdministrator) {
    Write-Host "Administrator permission is required to inspect and prepare Windows audio devices."
    Write-Host "Windows will display a UAC confirmation."
    $Elevated = Start-Process -FilePath $Application -ArgumentList $ArgumentLine -Verb RunAs -Wait -PassThru
    if (Test-Path -LiteralPath $ReportPath) {
        Write-Host "Installation report: $ReportPath"
    }
    exit $Elevated.ExitCode
}

Write-Host "UniversalAudioDriver setup"
Write-Host "Computer-specific hardware detection will run now."
Write-Host "Local matching driver packages: $DriverDirectory"
Write-Host "No driver signature checks will be disabled and no incompatible driver will be forced."
Write-Host ""

& $Application --install --driver-dir $DriverDirectory --report $ReportPath
$ExitCode = $LASTEXITCODE

if (Test-Path -LiteralPath $ReportPath) {
    Write-Host ""
    Write-Host "Detailed report saved to: $ReportPath"
}

if ($ExitCode -eq 0) {
    Write-Host "The active built-in speaker endpoint passed the software playback test."
}
elseif ($ExitCode -eq 2) {
    Write-Warning "No active built-in speaker endpoint was exposed by the Windows audio stack."
}
elseif ($ExitCode -eq 3) {
    Write-Warning "The speaker endpoint exists, but the playback test failed."
}
else {
    Write-Warning "The audio setup process returned exit code $ExitCode."
}

exit $ExitCode
