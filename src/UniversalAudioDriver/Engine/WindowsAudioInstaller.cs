using System.Security.Principal;
using UniversalAudioDriver.Discovery;
using UniversalAudioDriver.Models;

namespace UniversalAudioDriver.Engine;

/// <summary>
/// Installation-time detection and safe Windows audio preparation.
/// Only exact-ID local INF packages are offered to PnPUtil; Windows retains
/// signature validation and driver ranking.
/// </summary>
public sealed class WindowsAudioInstaller
{
    public AudioInstallationReport Install(string driverDirectory)
    {
        EnsureWindows();
        if (!IsAdministrator())
            throw new UnauthorizedAccessException(
                "Run Install-UniversalAudioDriver.ps1 as administrator. The installer must be elevated to rescan devices and start services.");

        var system = new WindowsSystemDiscovery().Scan();
        var discovery = new WindowsAudioDiscovery();
        var initialDevices = discovery.Scan();

        var packageResults = new SignedDriverPackageInstaller()
            .InstallMatchingPackages(driverDirectory, initialDevices);

        // Re-read the PnP problem state after local OEM packages were staged.
        // Windows Update is queried only for devices that still report a problem.
        var devicesAfterLocalPackages = discovery.Scan();
        var windowsUpdateResult = new WindowsUpdateDriverInstaller()
            .InstallMatchingUpdates(devicesAfterLocalPackages);

        // Ask Windows PnP to enumerate again after package staging.
        var pnpExitCode = new WindowsPnpRecovery().RescanDevices();

        var serviceResults = new WindowsAudioServiceManager().EnsureRunning();

        // Re-read devices and endpoints after the PnP scan/service recovery.
        var devices = discovery.Scan();
        var endpoints = new CoreAudioEndpointDiscovery().ScanRenderEndpoints();
        var assessments = new AudioCompatibilityEngine().Assess(devices);
        var speakerEndpoint = BuiltInSpeakerEndpointSelector.Select(endpoints);

        DefaultAudioEndpointConfigurationResult? defaultEndpointConfiguration = null;
        SpeakerTestResult speakerTest;
        if (speakerEndpoint is null)
        {
            speakerTest = new SpeakerTestResult(
                false, null, null, false,
                "Windows did not expose an active endpoint clearly identified as built-in speakers.");
        }
        else
        {
            defaultEndpointConfiguration = new WindowsDefaultAudioEndpointManager()
                .SetBuiltInSpeakersAsDefault(speakerEndpoint);

            try
            {
                var playback = new AdaptiveAudioOutput().PlayTestToneOnEndpoint(
                    speakerEndpoint.Id, TimeSpan.FromSeconds(1.2));
                speakerTest = new SpeakerTestResult(
                    true,
                    playback.EndpointName,
                    playback.EndpointId,
                    true,
                    $"Low-volume test tone submitted via {playback.ShareMode} at {playback.SampleRate} Hz / {playback.Channels} channel(s).");
            }
            catch (Exception ex)
            {
                speakerTest = new SpeakerTestResult(
                    true, speakerEndpoint.Name, speakerEndpoint.Id, false,
                    $"Speaker endpoint exists, but the test tone failed: {ex.Message}");
            }
        }

        if (defaultEndpointConfiguration?.SystemPlaybackDefaultChanged == true)
            endpoints = new CoreAudioEndpointDiscovery().ScanRenderEndpoints();

        var defaultPlaybackReady = defaultEndpointConfiguration?.SystemPlaybackDefaultChanged == true;
        var status = speakerTest.TonePlayed && defaultPlaybackReady
            ? "Ready: Windows accepted playback through the built-in speakers and they are now the system default output."
            : speakerTest.TonePlayed
                ? "Playback verified on the built-in speakers, but Windows did not confirm changing the system default output. Some apps may still use their existing/per-app route."
                : windowsUpdateResult.RebootRequired
                    ? "Restart required: Windows Update installed a matching audio driver and reports that Windows must restart before the device can be re-enumerated."
                    : speakerTest.EndpointFound
                        ? "Partially configured: a built-in speaker endpoint exists, but test playback failed."
                        : "Not ready: no active built-in speaker endpoint is exposed by the current Windows audio driver stack.";

        var nextStep = speakerTest.TonePlayed && defaultPlaybackReady
            ? "The built-in speaker endpoint passed the playback test and Windows accepted it as the Console and Multimedia default. The OEM kernel driver has not been replaced."
            : speakerTest.TonePlayed
                ? "The built-in speaker endpoint played the test tone, but the Windows default-device change was not confirmed. Review DefaultPlaybackEndpoint role HRESULTs in the JSON report."
                : windowsUpdateResult.RebootRequired
                    ? "Restart Windows, run the installer again, and check whether the built-in speaker endpoint now appears."
                    : "Check the report for PnP problem codes and missing platform dependencies. Add the laptop manufacturer's signed audio driver package to the Drivers folder and rerun the installer. A hardware-specific miniport/topology still has to exist for unsupported controllers/codecs.";

        return new AudioInstallationReport(
            system,
            devices.Count,
            assessments,
            endpoints,
            defaultEndpointConfiguration,
            serviceResults,
            packageResults,
            windowsUpdateResult,
            pnpExitCode,
            speakerTest,
            status,
            nextStep);
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Audio driver installation is available only on Windows.");
    }
}
