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
$ReportPathWasProvided = -not [string]::IsNullOrWhiteSpace($ReportPath)

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

    # Install the utility persistently under Program Files. Driver packages are
    # kept separately under ProgramData; PnPUtil stages accepted INF packages
    # into Windows Driver Store (DriverStore/FileRepository), not by raw copy.
    $InstallRoot = Join-Path ([Environment]::GetFolderPath("ProgramFiles")) "UniversalAudioDriver"
    $DataRoot = Join-Path ([Environment]::GetFolderPath("CommonApplicationData")) "UniversalAudioDriver"
    $InstalledApplication = Join-Path $InstallRoot "UniversalAudioDriver.exe"
    $InstalledDriverDirectory = Join-Path $DataRoot "Drivers"

    New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $InstalledDriverDirectory | Out-Null

    foreach ($name in @("UniversalAudioDriver.exe", "Install-UniversalAudioDriver.ps1", "Install-UniversalAudioDriver.cmd", "INSTALLATION-README.md")) {
        $source = Join-Path $ScriptDirectory $name
        $destination = Join-Path $InstallRoot $name
        if ((Test-Path -LiteralPath $source -PathType Leaf) -and
            [System.IO.Path]::GetFullPath($source) -ne [System.IO.Path]::GetFullPath($destination)) {
            Copy-Item -LiteralPath $source -Destination $destination -Force
        }
    }

    if ((Test-Path -LiteralPath $DriverDirectory -PathType Container) -and
        [System.IO.Path]::GetFullPath($DriverDirectory).TrimEnd('\') -ne
        [System.IO.Path]::GetFullPath($InstalledDriverDirectory).TrimEnd('\')) {
        Get-ChildItem -LiteralPath $DriverDirectory -Force | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $InstalledDriverDirectory -Recurse -Force
        }
    }

    $DriverDirectory = $InstalledDriverDirectory
    if (-not $ReportPathWasProvided) {
        $ReportPath = Join-Path $DataRoot "audio-install-report.json"
    }
    $LogPath = Join-Path $DataRoot "audio-install.log"
    $Application = $InstalledApplication

    if (-not (Test-Path -LiteralPath $Application -PathType Leaf)) {
        throw "The application could not be installed to Program Files: $Application"
    }

    $computer = Get-WmiObject Win32_ComputerSystem
    $os = Get-WmiObject Win32_OperatingSystem
    Write-Host "UniversalAudioDriver - automatic audio recovery"
    Write-Host ("Installed application: {0}" -f $Application)
    Write-Host ("Persistent driver packages: {0}" -f $DriverDirectory)
    Write-Host "Matching signed INF packages will be staged by PnPUtil into the Windows Driver Store."

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
