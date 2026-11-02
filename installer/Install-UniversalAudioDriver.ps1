[CmdletBinding()]
param(
    [AllowNull()]
    [AllowEmptyString()]
    [string]$DriverDirectory = "",
    [AllowNull()]
    [AllowEmptyString()]
    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"

# PSScriptRoot is empty in some legacy PowerShell invocation contexts.
$ScriptDirectory = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ScriptDirectory)) {
    if (-not [string]::IsNullOrWhiteSpace($MyInvocation.MyCommand.Path)) {
        $ScriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
    }
}
if ([string]::IsNullOrWhiteSpace($ScriptDirectory)) {
    $ScriptDirectory = (Get-Location).Path
}
$ScriptDirectory = [System.IO.Path]::GetFullPath($ScriptDirectory)

if ([string]::IsNullOrWhiteSpace($DriverDirectory)) {
    $DriverDirectory = Join-Path -Path $ScriptDirectory -ChildPath "Drivers"
}
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path -Path $ScriptDirectory -ChildPath "audio-install-report.json"
}
$DriverDirectory = [System.IO.Path]::GetFullPath($DriverDirectory)
$ReportPath = [System.IO.Path]::GetFullPath($ReportPath)

$Application = Join-Path -Path $ScriptDirectory -ChildPath "UniversalAudioDriver.exe"
$LogPath = Join-Path -Path $ScriptDirectory -ChildPath "audio-install.log"

function Write-FailureReport {
    param([string]$Message)
    try {
        $reportParent = Split-Path -Parent $ReportPath
        if (-not [string]::IsNullOrWhiteSpace($reportParent) -and
            -not (Test-Path -LiteralPath $reportParent -PathType Container)) {
            New-Item -ItemType Directory -Force -Path $reportParent | Out-Null
        }
        $computer = Get-WmiObject Win32_ComputerSystem -ErrorAction SilentlyContinue
        $os = Get-WmiObject Win32_OperatingSystem -ErrorAction SilentlyContinue
        [ordered]@{
            Status = "InstallerFailure"
            Timestamp = (Get-Date).ToString("o")
            ComputerManufacturer = $computer.Manufacturer
            ComputerModel = $computer.Model
            WindowsCaption = $os.Caption
            WindowsVersion = $os.Version
            OSArchitecture = $os.OSArchitecture
            DriverDirectory = $DriverDirectory
            Application = $Application
            Error = $Message
            Log = $LogPath
        } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
    }
    catch {
        Write-Warning "Could not write the JSON failure report: $($_.Exception.Message)"
    }
}

try {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw "This installer supports Windows only."
    }
    if (-not (Test-Path -LiteralPath $Application -PathType Leaf)) {
        throw "UniversalAudioDriver.exe was not found next to this installer: $Application"
    }

    $reportParent = Split-Path -Parent $ReportPath
    if (-not [string]::IsNullOrWhiteSpace($reportParent) -and
        -not (Test-Path -LiteralPath $reportParent -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $reportParent | Out-Null
    }
    if (-not (Test-Path -LiteralPath $DriverDirectory -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $DriverDirectory | Out-Null
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    $isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    if (-not $isAdministrator) {
        Write-Host "Administrator permission is required. Windows will ask for UAC approval."
        $arguments = @(
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", ('"{0}"' -f $PSCommandPath),
            "-DriverDirectory", ('"{0}"' -f $DriverDirectory),
            "-ReportPath", ('"{0}"' -f $ReportPath)
        )
        $elevated = Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -Verb RunAs -Wait -PassThru
        exit $elevated.ExitCode
    }

    $computer = Get-WmiObject Win32_ComputerSystem
    $os = Get-WmiObject Win32_OperatingSystem
    Write-Host "UniversalAudioDriver - automatic audio recovery"
    Write-Host ("Computer: {0} {1}" -f $computer.Manufacturer, $computer.Model)
    Write-Host ("Windows: {0} {1} ({2})" -f $os.Caption, $os.Version, $os.OSArchitecture)
    Write-Host ("Application: {0}" -f $Application)
    Write-Host ("Driver packages: {0}" -f $DriverDirectory)
    Write-Host ("Detailed log: {0}" -f $LogPath)
    Write-Host ""

    Write-Host "[1/2] Checking internal diagnostic tests..."
    & $Application --self-test 2>&1 | Tee-Object -FilePath $LogPath
    $selfTestExit = $LASTEXITCODE
    if ($selfTestExit -ne 0) {
        throw "Internal self-test failed with exit code $selfTestExit. See $LogPath."
    }

    Write-Host ""
    Write-Host "[2/2] Detecting audio hardware, checking signed driver packages and testing built-in speakers..."
    & $Application --install --driver-dir $DriverDirectory --report $ReportPath 2>&1 |
        Tee-Object -FilePath $LogPath -Append
    $installExit = $LASTEXITCODE

    if (Test-Path -LiteralPath $ReportPath -PathType Leaf) {
        Write-Host ("Detailed report: {0}" -f $ReportPath)
    }
    switch ($installExit) {
        0 { Write-Host "Windows accepted the speaker test stream and confirmed the default route. Listen to confirm physical sound." }
        2 { Write-Warning "No active built-in speaker endpoint is exposed. The exact laptop audio driver may be missing." }
        3 { Write-Warning "The speaker endpoint exists, but Windows could not submit the test stream." }
        4 { Write-Warning "A matching driver update requires restart. Restart Windows and run setup again." }
        5 { Write-Warning "The test stream was accepted, but the default output route could not be verified." }
        default { Write-Warning "Setup returned exit code $installExit. See $LogPath and $ReportPath." }
    }
    exit $installExit
}
catch {
    $message = $_.Exception.ToString()
    Write-Error $message
    Write-FailureReport -Message $message
    try {
        Add-Content -LiteralPath $LogPath -Value ([Environment]::NewLine + "INSTALLER ERROR:" + [Environment]::NewLine + $message)
    } catch { }
    exit 10
}
