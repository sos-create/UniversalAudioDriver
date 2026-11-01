# Initial architecture

## Design principle

Separate device discovery and diagnosis from any operation that changes the Windows driver stack.

## Planned components

- **Inventory**: enumerate Plug and Play devices and audio endpoints.
- **Identification**: normalize hardware IDs and map known controllers/codecs to documented families.
- **Diagnostics**: correlate device status, problem codes, driver metadata, audio services, and endpoint availability.
- **Compatibility database**: store verified device-family notes and links to official driver sources.
- **Repair orchestration**: later, offer only reversible, user-approved actions.
- **UI**: present findings and explain what is known, unknown, and safe to try.

## Important Windows audio distinction

A Windows playback endpoint is not the same thing as a physical speaker. A laptop may expose an HDA or Intel SST controller, a codec, DSP/software components, amplifier controls, and one or more endpoint devices. The built-in speaker path can fail even when Windows reports that an audio controller is present.

The project must identify the full device chain before proposing a fix. It must not install a generic kernel driver over a vendor driver based only on a device name.

## Current milestone

The PowerShell collector is read-only and targets Windows 10/11. Its report is the input for the first hardware-identification rules.
