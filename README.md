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

The published Windows bundle includes a one-click launcher,
`Install-UniversalAudioDriver.cmd`. Double-click it and approve the Windows UAC
prompt. The setup then:

1. Reads the computer model, processor, BIOS, Windows version and audio PnP
   devices, including hardware IDs, compatible IDs and parent controller IDs.
   The component report distinguishes controller/DSP, codec, speaker amplifier
   (for example CS35/MAX98/TAS/RT101 families) and audio software components.
   It also includes audio-related parent controllers in the System PnP class
   when they are part of an audio device's ancestry and report a driver problem.
2. Scans the local `Drivers` folder for INF packages containing exact matching
   identifiers. Matching packages are submitted to Windows with PnPUtil.
3. Re-scans devices that still report a PnP problem and queries Windows Update
   Agent for driver updates whose declared `DriverHardwareID` exactly matches
   one of those hardware, compatible or parent IDs. Updates requiring an
   unaccepted EULA are skipped.
4. Lets Windows enforce package signatures and driver ranking. The installer
   never uses `/force`, disables driver-signature enforcement, or disables
   Secure Boot.
5. Requests a PnP rescan and starts the standard Windows audio services when
   possible.
6. Uses the native Windows endpoint form-factor property, with localized-name
   fallback, to identify built-in speakers. It attempts to set the speakers as
   the Console, Multimedia and Communications default output, then submits a
   short low-volume test tone directly to that endpoint.
7. Writes `audio-install-report.json` with system information, all device IDs,
   parent-controller IDs, detected backend profile, matching local/WU packages,
   service state, default-output HRESULTs and the speaker playback result.

The default-output change uses Windows' undocumented `IPolicyConfig` COM
interface because the public Core Audio MMDevice API does not expose a default
endpoint setter. This is best-effort: Windows can reject the operation, and
per-application routing preferences can still override the system default.
The report records the HRESULT for each audio role.

To add a laptop manufacturer's driver package, place its complete signed audio
package in the `Drivers` folder and run setup again. Keep the INF, catalog,
SYS, firmware and companion files together. Windows can still reject a package
or decide that another driver has a better rank. When Windows Update reports
that a matching driver requires a restart, the installer returns a restart
status; restart Windows and run it again to verify the speaker endpoint.

This installation utility can configure and verify an already supported
Windows speaker path, and can retrieve matching OEM/Windows Update packages.
It cannot synthesize a missing Intel SST/AMD ACP/HDA/SoundWire kernel miniport,
codec driver, DSP firmware, amplifier driver or OEM ACPI topology from generic
blocks. Those components must exist for the exact hardware, or be implemented
as a hardware-specific driver module and validated on the actual laptop. A
successful test tone confirms that Windows accepted playback on the selected
endpoint; it does not qualify every speaker, jack, microphone, sleep/resume or
power-state path.
