[CmdletBinding()]
param(
    [string]$DriverDirectory = (Join-Path $PSScriptRoot "Drivers"),
    [string]$ReportPath = (Join-Path $PSScriptRoot "audio-install-report.json")
)

$ErrorActionPreference = "Stop"
$Application = Join-Path $PSScriptRoot "UniversalAudioDriver.exe"
$LogPath = Join-Path $PSScriptRoot "audio-install.log"

function Write-FailureReport {
    param([string]$Message)
    try {
        $computer = Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
        [ordered]@{
            Status = "InstallerFailure"
            Timestamp = (Get-Date).ToString("o")
            ComputerManufacturer = $computer.Manufacturer
            ComputerModel = $computer.Model
            WindowsCaption = $os.Caption
            WindowsVersion = $os.Version
            OSArchitecture = $os.OSArchitecture
            ProcessArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
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
    if ($reportParent) {
        New-Item -ItemType Directory -Force -Path $reportParent | Out-Null
    }
    New-Item -ItemType Directory -Force -Path $DriverDirectory | Out-Null

    $Identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $Principal = [Security.Principal.WindowsPrincipal]::new($Identity)
    $IsAdministrator = $Principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    if (-not $IsAdministrator) {
        Write-Host "Administrator permission is required. Windows will ask for UAC approval."
        $psArgs = @(
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", ('"{0}"' -f $PSCommandPath),
            "-DriverDirectory", ('"{0}"' -f $DriverDirectory),
            "-ReportPath", ('"{0}"' -f $ReportPath)
        )
        $elevated = Start-Process -FilePath "powershell.exe" -ArgumentList $psArgs -Verb RunAs -Wait -PassThru
        exit $elevated.ExitCode
    }

    $computer = Get-CimInstance Win32_ComputerSystem
    $os = Get-CimInstance Win32_OperatingSystem
    Write-Host "UniversalAudioDriver - automatic audio recovery"
    Write-Host "Computer: $($computer.Manufacturer) $($computer.Model)"
    Write-Host "Windows: $($os.Caption) $($os.Version) ($($os.OSArchitecture))"
    Write-Host "Application: $Application"
    Write-Host "Driver packages: $DriverDirectory"
    Write-Host "Detailed log: $LogPath"
    Write-Host ""

    Write-Host "[1/2] Checking the built-in diagnostic tests..."
    & $Application --self-test 2>&1 | Tee-Object -FilePath $LogPath
    $selfTestExit = $LASTEXITCODE
    if ($selfTestExit -ne 0) {
        throw "Internal self-test failed with exit code $selfTestExit. See $LogPath."
    }

    Write-Host ""
    Write-Host "[2/2] Detecting hardware, checking signed driver packages, repairing Windows audio services and testing speaker output..."
    & $Application --install --driver-dir $DriverDirectory --report $ReportPath 2>&1 |
        Tee-Object -FilePath $LogPath -Append
    $installExit = $LASTEXITCODE

    if (Test-Path -LiteralPath $ReportPath) {
        Write-Host ""
        Write-Host "Detailed report: $ReportPath"
    }

    switch ($installExit) {
        0 { Write-Host "Windows accepted the speaker test stream and confirmed the default route. Physical sound must still be checked by listening." }
        2 { Write-Warning "No active built-in speaker endpoint is exposed. The hardware-specific controller/codec driver may be missing." }
        3 { Write-Warning "The speaker endpoint exists, but Windows could not submit the test stream. See the report." }
        4 { Write-Warning "A matching driver update requires a restart. Restart Windows and run setup again." }
        5 { Write-Warning "The test stream was accepted, but Windows did not confirm the default output change. See the report." }
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
    }
    catch { }
    exit 10
}
