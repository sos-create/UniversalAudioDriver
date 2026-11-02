# Local signed driver packages

Place complete OEM driver packages for this exact laptop model in this folder.
Keep the original INF, CAT, SYS, DLL and firmware files together and preserve
the manufacturer's directory structure.

During installation UniversalAudioDriver:
1. Detects audio-related PnP devices and their full hardware IDs.
2. Looks for INF files that contain an exact device/hardware ID token.
3. Passes only those matching INF files to Windows PnPUtil with /add-driver /install.
4. Leaves package signature verification and driver ranking to Windows.
5. Rescans Plug and Play and checks whether an active built-in speaker endpoint appears.

Unsigned packages are not allowed by this installer. It does not use /force,
disable driver-signature enforcement, disable Secure Boot, or replace a better-ranked
OEM driver.

No third-party driver packages are bundled here. A missing hardware-specific
controller/codec/amplifier package must come from the laptop manufacturer.
