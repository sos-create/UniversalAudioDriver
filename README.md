# Universal Audio Driver

**Status: early prototype — diagnostic foundation**

A Windows audio compatibility and troubleshooting project focused on detecting laptop audio hardware, identifying missing or misconfigured components, and guiding safe recovery of built-in speakers.

> This project does not yet contain a kernel-mode audio driver. A truly universal driver cannot safely replace vendor-specific codec, amplifier, DSP, firmware, and jack-detection support. The project will first identify the hardware and use documented Windows audio interfaces and vendor-supported drivers wherever possible.

## Goals

- Detect audio controllers, codecs, audio endpoints, and relevant Plug and Play devices.
- Collect hardware IDs, compatible IDs, driver provider/version, device status, and problem codes.
- Identify likely causes of missing built-in speaker output.
- Diagnose Windows audio services and default playback endpoints.
- Recommend compatible, signed vendor or Microsoft drivers.
- Add vendor-specific compatibility modules only when hardware documentation and licensing permit.
- Support Windows 10 and Windows 11 where APIs and drivers allow.

## Roadmap

1. **Diagnostics foundation** — read-only PowerShell inventory of audio devices and endpoints.
2. **Hardware identification** — classify Intel SST, Realtek, Conexant, Cirrus Logic, AMD/NVIDIA HDMI audio, and other devices from their IDs.
3. **Troubleshooting engine** — explain device errors, missing endpoints, service state, and default-output problems.
4. **Safe repair workflows** — explicit, reversible actions with backups and user confirmation.
5. **Compatibility modules** — vendor-specific handling based on verified hardware support.
6. **User interface and packaging** — a simple Windows application and signed distribution plan.

## First test

On the affected Windows 10 laptop:

1. Open PowerShell.
2. Run:
   powershell -ExecutionPolicy Bypass -File .\diagnostics\collect_audio_report.ps1
3. The script writes a timestamped report to the Desktop.
4. Share the report (remove any personal information first) so we can identify the exact audio controller, codec, endpoints, and driver errors.

The initial collector is **read-only**: it does not install, remove, enable, disable, or modify drivers or audio settings.

## Safety

Kernel-mode drivers can crash Windows or disable audio hardware. No unsigned driver installation, forced hardware-ID matching, registry edits, or firmware changes will be attempted by the diagnostic tool. Any future repair operation must be narrowly scoped, reversible, and confirmed by the user.

## Development

The first milestone is accurate hardware detection. A reliable device-specific diagnosis is more useful than pretending one binary can drive every laptop speaker design.
