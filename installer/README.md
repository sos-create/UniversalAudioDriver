# UniversalAudioDriver installation bundle

1. Extract the complete ZIP.
2. Keep UniversalAudioDriver.exe, Install-UniversalAudioDriver.ps1 and the
   Drivers folder together.
3. Double-click Install-UniversalAudioDriver.cmd (or run the PowerShell script
   directly with:
   powershell -ExecutionPolicy Bypass -File .\Install-UniversalAudioDriver.ps1).
4. Approve the Windows UAC prompt.
5. Read audio-install-report.json after setup.

The setup detects the laptop platform and audio PnP devices, starts the standard
Windows audio services when possible, rescans devices, and checks for an active
built-in speaker endpoint. It stages local, exact-ID matching INF packages through Windows PnPUtil in
dependency order: platform controller, audio transport/DSP, codec/amplifier,
then OEM extension/software-component packages. When no built-in speaker
endpoint is exposed, it searches the full detected internal audio stack and
also queries Windows Update Agent for driver updates whose declared
DriverHardwareID exactly matches a detected hardware, compatible or parent
controller ID. External USB, Bluetooth and HDMI audio paths are excluded from
this expanded recovery search. Updates requiring an unaccepted EULA are
skipped. Windows enforces package signature validation and driver ranking; no
forced installation is performed.

This bundle is not a universal kernel audio miniport. If no active built-in speaker
endpoint is exposed, the report identifies hardware IDs and missing dependencies.
A matching OEM controller/codec/amplifier driver or a hardware-specific miniport
and topology implementation is still required. When Windows Update installs a
matching driver and reports that a restart is required, the installer returns
a restart-required status; restart Windows and run the installer again to verify
the speaker endpoint. A short WASAPI test confirms that Windows accepted an audio stream
on the selected endpoint; it cannot measure acoustic output or prove every
speaker, jack, microphone, power-state or sleep/resume path.
