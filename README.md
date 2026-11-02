# UniversalAudioDriver — driver development status

This repository contains two separate parts:

1. **Windows discovery and recovery utility** — a self-contained .NET application that inventories audio-related Plug and Play devices, Windows audio services and Core Audio playback endpoints. It can request a standard Plug and Play rescan and optionally play a short test tone through the current default endpoint.
2. **Kernel-driver development foundation** — scripts to obtain a pinned revision of Microsoft's official SysVAD virtual-audio sample and build it with Visual Studio + the matching Windows Driver Kit (WDK).

## Important limitation

The current repository does **not** yet contain a finished UniversalAudioDriver kernel package. The Microsoft SysVAD sample is a virtual audio driver foundation; it does not replace a laptop's Intel SST/AMD ACP controller, codec, amplifier, DSP, ACPI or firmware-specific components, and it does not guarantee sound from physical speakers.

A real hardware-support package requires a tested miniport/topology implementation for the target hardware, a valid INF/package catalog, appropriate signing, and testing on the actual device. Do not force-install this virtual sample over an existing hardware driver.

## Build the standalone utility

GitHub Actions publishes self-contained Windows executables for x64, x86 and ARM64. The target PC does not need the .NET runtime installed.

Open **Actions → Publish standalone Windows app**, select the latest successful run, and download the artifact matching the PC architecture.

## Prepare the Microsoft SysVAD source

Requirements on a development PC:
- Git for Windows
- Visual Studio with Desktop development with C++ / MSBuild
- Windows Driver Kit (WDK) matching the installed Visual Studio and target Windows SDK

From the repository root, run in PowerShell:

```powershell
.\driver\prepare-sysvad.ps1
.\driver\build-sysvad.ps1 -Configuration Release -Platform x64
```

The source is pinned to Microsoft Windows-driver-samples commit:
`f4c1ff842b6a0001152f44cb61788b216a1c7627`.

The build script sets `SignMode=Off` for compilation only. It does not produce a production-signed package and does not install a driver.

## Utility commands

```powershell
.\UniversalAudioDriver.exe
.\UniversalAudioDriver.exe --rescan-pnp
.\UniversalAudioDriver.exe --test-tone
```

The rescan command requests a normal Windows PnP rescan. The test-tone command intentionally plays a brief low-volume tone through the current default playback endpoint.
