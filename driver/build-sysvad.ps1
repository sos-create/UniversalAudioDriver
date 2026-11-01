[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("Win32", "x64", "ARM64")]
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

$Solution = Join-Path $PSScriptRoot "_vendor\Windows-driver-samples\audio\sysvad\sysvad.sln"
$PrepareScript = Join-Path $PSScriptRoot "prepare-sysvad.ps1"

if (-not (Test-Path $Solution)) {
    & $PrepareScript
    if ($LASTEXITCODE -ne 0) { throw "Could not prepare the Microsoft SysVAD source." }
}

if (-not (Test-Path $Solution)) {
    throw "SysVAD solution was not found. Check the Microsoft sample checkout."
}

$MsBuild = Get-Command msbuild.exe -ErrorAction SilentlyContinue
if (-not $MsBuild) {
    $VsWhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $VsWhere) {
        $VsPath = & $VsWhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
        if ($VsPath) {
            $Candidate = Join-Path $VsPath "MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path $Candidate) { $MsBuild = Get-Item $Candidate }
        }
    }
}

if (-not $MsBuild) {
    throw "MSBuild was not found. Install Visual Studio Build Tools with C++ tools and the Windows Driver Kit (WDK)."
}

Write-Host "Building Microsoft SysVAD foundation: $Configuration / $Platform"
& $MsBuild.Source $Solution /m /p:Configuration=$Configuration /p:Platform=$Platform /p:SignMode=Off /verbosity:minimal
if ($LASTEXITCODE -ne 0) {
    throw "SysVAD build failed with exit code $LASTEXITCODE. Check that the matching WDK and C++ build tools are installed."
}

Write-Host "SysVAD build completed. This is Microsoft's virtual audio sample foundation, not yet the UniversalAudioDriver hardware replacement."
