# Local signed driver packages

Place complete OEM driver packages for this exact laptop model in this folder.
Keep the original INF, CAT, SYS, DLL and firmware files together and preserve
the manufacturer's directory structure.

During installation UniversalAudioDriver:

1. Detects audio-related PnP devices, their exact hardware/compatible IDs, parent
   controller IDs and the exposed built-in speaker endpoint.
2. If no built-in speaker endpoint exists, searches the complete detected audio
   stack for local OEM packages. If speakers are already exposed, it limits local
   package installation to devices reporting recoverable PnP problem codes.
3. Finds INF files that contain a complete exact device, compatible or parent
   controller ID token. Short vendor/product prefixes are not accepted.
4. Plans package staging in dependency order: platform controller, audio
   transport/DSP, codec/amplifier, then OEM extension/software-component packages.
5. Passes only matching INF files to Windows PnPUtil with /add-driver /install.
6. Lets Windows validate signatures, applicability and driver rank, then rescans
   Plug and Play and checks for a built-in speaker endpoint.

Windows may reject a package or decide that its rank is not suitable. The planner
does not modify INF files, fabricate SYS binaries, bypass signature checks, or
force an incompatible package onto a device.

No third-party driver packages are bundled here. Include the complete signed
audio/chipset package from the laptop manufacturer to let the installer stage
the correct controller, codec and OEM extension components as a bundle.
