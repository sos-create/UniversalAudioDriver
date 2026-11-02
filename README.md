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
.\UniversalAudioDriver.exe --test-speakers
```

The rescan command requests a normal Windows PnP rescan. The test-tone command intentionally plays a brief low-volume tone through the current default playback endpoint. The `--test-speakers` command specifically locates the active built-in speaker endpoint, attempts to make it the Windows Console and Multimedia default, submits a low-volume WASAPI test stream directly to that endpoint, and verifies the default route by reading it back from Core Audio. `--adaptive-test` selects an active render endpoint (preferring the Windows default multimedia endpoint), reads its shared-mode mix format, and plays a low-volume test tone using that format.


## Adaptive output foundation (current implementation)

The adaptive output path uses Windows Core Audio/WASAPI in shared mode. It selects an active render endpoint, prefers the default multimedia endpoint, reads the endpoint's current mix sample rate/channel count, and configures playback to those values. This can send audio through the laptop's physical speakers **when the installed Windows controller/codec/amplifier driver stack already exposes those speakers as an active render endpoint**.

This is not yet a custom kernel-mode audio driver. It does not replace Intel SST, AMD ACP, Realtek, Cirrus Logic, codec, amplifier, ACPI or DSP drivers. A hardware-specific kernel miniport/topology module cannot be implemented or safely tested without the target controller/codec IDs and platform routing information. The project deliberately does not overwrite existing vendor drivers or install a virtual sample as a physical-speaker driver.


## Persistent Windows installation

The one-click installer deploys the recovery utility to
`%ProgramFiles%\\UniversalAudioDriver` and keeps its local signed OEM packages
and reports in `%ProgramData%\\UniversalAudioDriver`. It does not place an
ordinary EXE in the Windows kernel folders.

Matched OEM INF packages are passed to Windows PnPUtil
(`pnputil /add-driver ... /install`). PnPUtil stages accepted packages in the
protected Windows Driver Store, normally
`%SystemRoot%\\System32\\DriverStore\\FileRepository`, and Windows Plug and
Play decides whether a signed package applies and binds it to each device.
The utility prints the resulting PnP driver provider, version, INF name and
problem code so that a staged package is not confused with a successfully
bound/working device driver.

This is a persistent, standard Windows driver installation path for matched
OEM packages. It is **not** a universal custom kernel audio miniport; it cannot
create missing hardware-specific controller, codec, DSP or amplifier drivers.

## Installation-time detection and preparation

The published Windows bundle includes a one-click launcher,
`Install-UniversalAudioDriver.cmd`. Double-click it and approve the Windows UAC
prompt. The setup then:

1. Reads the computer model, processor, BIOS, Windows version and audio PnP
   devices, including hardware IDs, compatible IDs and parent controller IDs.
   It detects common HDA, Intel SST, AMD ACP, SoundWire/SDCA, SLIMbus, I2S/I2C,
   USB, Bluetooth and HDMI audio paths. The codec catalog recognises Realtek
   ALC/RT, Cirrus CS35/CS42/CS47, Conexant CX, Qualcomm WCD, Nuvoton NAU,
   Everest ES, Texas Instruments TAS, Maxim MAX, Dialog DA and AKM families,
   plus vendor IDs for Analog Devices, IDT, ESS, SigmaTel, VIA, C-Media and
   Creative. It separates controller/DSP, codec, smart amplifier and audio
   software roles, and follows parent links to upstream audio dependencies.
2. Detects whether Windows already exposes the built-in speaker endpoint. If it
   does not, scans the complete discovered audio stack for exact-ID matches in
   the local `Drivers` folder; otherwise it limits local package staging to
   devices with recoverable PnP problem codes.
3. Orders matching OEM INF packages by dependency layer: platform controller,
   audio transport/DSP, codec/amplifier, then OEM extension/software-component.
   Windows PnPUtil performs signature, applicability and driver-rank checks.
4. Re-scans devices that still report a PnP problem and queries Windows Update
   Agent for driver updates whose declared `DriverHardwareID` exactly matches
   one of those hardware, compatible or parent IDs. Updates requiring an
   unaccepted EULA are skipped.
5. Lets Windows enforce package signatures and driver ranking. The installer
   never uses `/force`, disables driver-signature enforcement, or disables
   Secure Boot.
6. Requests a PnP rescan and starts the standard Windows audio services when
   possible.
7. Uses the native Windows endpoint form-factor property, with localized-name
   fallback, to identify built-in speakers. Before playback, it attempts to set
   that endpoint as the Console, Multimedia and Communications default output,
   so Windows system sounds and applications are routed there unless an app has
   its own output override. It then unmutes the selected endpoint and raises its
   endpoint volume to 20% only when it was muted or below that conservative floor
   (higher existing volume levels are left unchanged), before submitting a short
   low-volume WASAPI test stream. It reports the volume/mute operation; it still
   cannot measure acoustic output from the physical speaker cones.
8. Writes `audio-install-report.json` with system information, all device IDs,
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
as a hardware-specific driver module and validated on the actual laptop. A successful WASAPI test confirms that Windows accepted an audio stream on the
selected endpoint; it cannot prove that sound physically came out of the speaker
cones or qualify every speaker, jack, microphone, sleep/resume or power-state path.
