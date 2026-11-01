[CmdletBinding()]
param(
    [string]$OutputDirectory = [Environment]::GetFolderPath("Desktop")
)

$ErrorActionPreference = "Continue"

function Write-Section {
    param([string]$Title)
    "`r`n===== $Title =====" | Out-File -FilePath $script:ReportPath -Append -Encoding utf8
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$script:ReportPath = Join-Path $OutputDirectory "UniversalAudioDriver-$timestamp.txt"

"Universal Audio Driver - Read-only diagnostics" | Out-File -FilePath $script:ReportPath -Encoding utf8
"Created: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss K')" | Out-File -FilePath $script:ReportPath -Append -Encoding utf8
"Computer: $env:COMPUTERNAME" | Out-File -FilePath $script:ReportPath -Append -Encoding utf8

Write-Section "Windows"
try {
    Get-CimInstance Win32_OperatingSystem |
        Select-Object Caption, Version, BuildNumber, OSArchitecture |
        Format-List | Out-String |
        Out-File -FilePath $script:ReportPath -Append -Encoding utf8
} catch {
    "Could not read Windows version: $($_.Exception.Message)" | Out-File -FilePath $script:ReportPath -Append -Encoding utf8
}

Write-Section "Computer model"
try {
    Get-CimInstance Win32_ComputerSystem |
        Select-Object Manufacturer, Model, SystemType |
        Format-List | Out-String |
        Out-File -FilePath $script:ReportPath -Append -Encoding utf8
} catch {
    "Could not read computer model: $($_.Exception.Message)" | Out-File -FilePath $script:ReportPath -Append -Encoding utf8
}

Write-Section "Sound devices (WMI)"
try {
    Get-CimInstance Win32_SoundDevice |
        Select-Object Name, Manufacturer, Status, PNPDeviceID, ConfigManagerErrorCode |
        Format-List | Out-String -Width 240 |
        Out-File -FilePath $script:ReportPath -Append -Encoding utf8
} catch {
    "Could not enumerate Win32_SoundDevice: $($_.Exception.Message)" | Out-File -FilePath $script:ReportPath -Append -Encoding utf8
}

Write-Section "Plug and Play audio-related devices"
try {
    if (Get-Command Get-PnpDevice -ErrorAction SilentlyContinue) {
        Get-PnpDevice -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Class -in @("Media", "AudioEndpoint", "SoftwareComponent") -or
                $_.FriendlyName -match "audio|sound|speaker|realtek|conexant|cirrus|smart sound|sst|high definition"
            } |
            Select-Object Status, Class, FriendlyName, InstanceId, Problem |
            Format-List | Out-String -Width 240 |
            Out-File -FilePath $script:ReportPath -Append -Encoding utf8
    } else {
        "Get-PnpDevice is unavailable on this Windows installation." | Out-File -FilePath $script:ReportPath -Append -Encoding utf8
    }
} catch {
    "Could not enumerate PnP devices: $($_.Exception.Message)" | Out-File -FilePath $script:ReportPath -Append -Encoding utf8
}

Write-Section "Detailed audio-related PnP properties"
try {
    if (Get-Command Get-PnpDevice -ErrorAction SilentlyContinue) {
        $devices = Get-PnpDevice -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Class -in @("Media", "AudioEndpoint", "SoftwareComponent") -or
                $_.FriendlyName -match "audio|sound|speaker|realtek|conexant|cirrus|smart sound|sst|high definition"
            }

        foreach ($device in $devices) {
            "`r`n--- $($device.FriendlyName) ---" | Out-File -FilePath $script:ReportPath -Append -Encoding utf8
            "InstanceId: $($device.InstanceId)" | Out-File -FilePath $script:ReportPath -Append -Encoding utf8
            try {
                Get-PnpDeviceProperty -InstanceId $device.InstanceId -ErrorAction Stop |
                    Where-Object { $_.KeyName -match "HardwareIds|CompatibleIds|DriverVersion|DriverProvider|DriverInfPath|ProblemCode|Service" } |
                    Select-Object KeyName, Data |
                    Format-List | Out-String -Width 240 |
                    Out-File -FilePath $script:ReportPath -Append -Encoding utf8
            } catch {
                "Could not read device properties: $($_.Exception.Message)" | Out-File -FilePath $script:ReportPath -Append -Encoding utf8
            }
        }
    }
} catch {
    "Detailed PnP property collection failed: $($_.Exception.Message)" | Out-File -FilePath $script:ReportPath -Append -Encoding utf8
}

Write-Section "Audio services"
try {
    Get-Service -Name Audiosrv, AudioEndpointBuilder -ErrorAction SilentlyContinue |
        Select-Object Name, DisplayName, Status, StartType |
        Format-Table -AutoSize | Out-String |
        Out-File -FilePath $script:ReportPath -Append -Encoding utf8
} catch {
    "Could not read audio service state: $($_.Exception.Message)" | Out-File -FilePath $script:ReportPath -Append -Encoding utf8
}

Write-Section "Default playback devices (optional AudioDeviceCmdlets module)"
try {
    if (Get-Command Get-AudioDevice -ErrorAction SilentlyContinue) {
        Get-AudioDevice -List | Format-List | Out-String -Width 240 |
            Out-File -FilePath $script:ReportPath -Append -Encoding utf8
    } else {
        "Optional AudioDeviceCmdlets module is not installed; this collector does not install it." | Out-File -FilePath $script:ReportPath -Append -Encoding utf8
    }
} catch {
    "Could not query optional playback-device information: $($_.Exception.Message)" | Out-File -FilePath $script:ReportPath -Append -Encoding utf8
}

Write-Section "End of report"
"Collection completed. No system settings or drivers were changed." | Out-File -FilePath $script:ReportPath -Append -Encoding utf8

Write-Host ""
Write-Host "Audio diagnostic report created:" -ForegroundColor Green
Write-Host $script:ReportPath
Write-Host "The collector is read-only; no drivers or audio settings were changed."
