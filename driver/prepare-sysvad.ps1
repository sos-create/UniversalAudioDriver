[CmdletBinding()]
param(
    [string]$VendorDirectory = (Join-Path $PSScriptRoot "_vendor")
)

$ErrorActionPreference = "Stop"

$RepositoryUrl = "https://github.com/microsoft/Windows-driver-samples.git"
$PinnedCommit = "f4c1ff842b6a0001152f44cb61788b216a1c7627"
$Checkout = Join-Path $VendorDirectory "Windows-driver-samples"
$SysvadDirectory = Join-Path $Checkout "audio\sysvad"

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "Git for Windows is required. Install Git and run this script again."
}

New-Item -ItemType Directory -Force -Path $VendorDirectory | Out-Null

if (-not (Test-Path (Join-Path $Checkout ".git"))) {
    git clone --filter=blob:none --no-checkout $RepositoryUrl $Checkout
    if ($LASTEXITCODE -ne 0) { throw "Could not clone the Microsoft Windows driver samples repository." }

    git -C $Checkout sparse-checkout init --cone
    if ($LASTEXITCODE -ne 0) { throw "Could not initialize sparse checkout." }

    git -C $Checkout sparse-checkout set audio/sysvad wil
    if ($LASTEXITCODE -ne 0) { throw "Could not select the SysVAD source tree." }

    git -C $Checkout fetch --depth 1 origin $PinnedCommit
    if ($LASTEXITCODE -ne 0) { throw "Could not fetch the pinned Microsoft sample revision." }

    git -C $Checkout checkout --detach $PinnedCommit
    if ($LASTEXITCODE -ne 0) { throw "Could not check out the pinned Microsoft sample revision." }
}
else {
    git -C $Checkout fetch --depth 1 origin $PinnedCommit
    if ($LASTEXITCODE -ne 0) { throw "Could not update the pinned Microsoft sample revision." }
    git -C $Checkout checkout --detach $PinnedCommit
    if ($LASTEXITCODE -ne 0) { throw "Could not check out the pinned Microsoft sample revision." }
}

git -C $Checkout submodule update --init --depth 1 -- wil
if ($LASTEXITCODE -ne 0) {
    Write-Warning "WIL submodule was not initialized. Some SysVAD configurations may not build."
}

if (-not (Test-Path $SysvadDirectory)) {
    throw "The SysVAD source directory was not found: $SysvadDirectory"
}

Write-Host "Microsoft SysVAD source is ready:"
Write-Host $SysvadDirectory
Write-Host "Pinned revision: $PinnedCommit"
