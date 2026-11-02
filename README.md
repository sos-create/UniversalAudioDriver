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

The rescan command requests a normal Windows PnP rescan. The test-tone command intentionally plays a brief low-volume tone through the current default playback endpoint. `--adaptive-test` selects an active render endpoint (preferring the Windows default multimedia endpoint), reads its shared-mode mix format, and plays a low-volume test tone using that format.


## Adaptive output foundation (current implementation)

The adaptive output path uses Windows Core Audio/WASAPI in shared mode. It selects an active render endpoint, prefers the default multimedia endpoint, reads the endpoint's current mix sample rate/channel count, and configures playback to those values. This can send audio through the laptop's physical speakers **when the installed Windows controller/codec/amplifier driver stack already exposes those speakers as an active render endpoint**.

This is not yet a custom kernel-mode audio driver. It does not replace Intel SST, AMD ACP, Realtek, Cirrus Logic, codec, amplifier, ACPI or DSP drivers. A hardware-specific kernel miniport/topology module cannot be implemented or safely tested without the target controller/codec IDs and platform routing information. The project deliberately does not overwrite existing vendor drivers or install a virtual sample as a physical-speaker driver.


## Installation-time detection and preparation

The published installer bundle contains Install-UniversalAudioDriver.ps1. Run it
from the extracted bundle; it requests administrator permission and then:

1. Reads the computer model, BIOS, processor and audio PnP hardware IDs.
2. Scans the local Drivers folder for INF packages containing exact device IDs.
3. Offers only those packages to Windows PnPUtil using /add-driver /install.
4. Lets Windows enforce catalog signature checks and driver ranking; it never
   uses /force or disables driver-signature enforcement.
5. Requests a standard PnP rescan and starts AudioEndpointBuilder/Audiosrv if
   they are stopped and not disabled.
6. Searches for an active render endpoint clearly named as built-in speakers and
   submits a brief low-volume test tone to that endpoint.
7. Writes audio-install-report.json beside the executable.

To add a laptop manufacturer's package, place its complete signed audio driver
package in the Drivers folder and run the installer again. The INF must identify
one of the detected hardware or compatible IDs. Windows can still reject a
package or decide that another driver has a better rank. If Windows Update
installs a matching driver and requires a restart, the installer reports that
state; restart and run it again to verify the built-in speaker endpoint.

This installer can configure and verify an already supported Windows speaker
path. It cannot synthesize a missing Intel SST/AMD ACP/HDA/SoundWire kernel
miniport, codec driver, DSP firmware, amplifier driver or OEM ACPI topology from
generic blocks. Those pieces must exist for the exact hardware. A successful
software test tone is evidence that Windows accepted playback on the selected
endpoint, not a complete hardware qualification of every speaker, jack,
microphone, sleep/resume and power-state path.
