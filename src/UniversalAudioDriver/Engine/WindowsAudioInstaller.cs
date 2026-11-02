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

        // Let Windows apply any newly staged local OEM packages before deciding
        // which devices still need a driver from Windows Update.
        int? localDriverPnpRescanExitCode = null;
        if (packageResults.Any(package => package.ExitCode == 0))
            localDriverPnpRescanExitCode = new WindowsPnpRecovery().RescanDevices();

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
                false, null, null, null, null, false,
                "Windows did not expose an active endpoint clearly identified as built-in speakers.");
        }
        else
        {
            var endpointControllerId = speakerEndpoint.ControllerDeviceId;
            var linkedPnpDevice = string.IsNullOrWhiteSpace(endpointControllerId)
                ? null
                : devices.FirstOrDefault(device =>
                    string.Equals(device.DeviceId, endpointControllerId, StringComparison.OrdinalIgnoreCase))
                  ?? devices.FirstOrDefault(device =>
                    device.ParentDeviceIds.Contains(endpointControllerId, StringComparer.OrdinalIgnoreCase));

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
                    endpointControllerId,
                    linkedPnpDevice?.Name,
                    true,
                    $"Low-volume test tone submitted via {playback.ShareMode} at {playback.SampleRate} Hz / {playback.Channels} channel(s).");
            }
            catch (Exception ex)
            {
                speakerTest = new SpeakerTestResult(
                    true, speakerEndpoint.Name, speakerEndpoint.Id,
                    endpointControllerId, linkedPnpDevice?.Name, false,
                    $"Speaker endpoint exists, but the test tone failed: {ex.Message}");
            }
        }

        if (defaultEndpointConfiguration?.Attempted == true)
        {
            // Core Audio may publish role changes shortly after IPolicyConfig returns.
            // Poll the actual defaults instead of trusting only the COM HRESULT.
            for (var attempt = 0; attempt < 8; attempt++)
            {
                endpoints = new CoreAudioEndpointDiscovery().ScanRenderEndpoints();
                defaultEndpointConfiguration = WindowsDefaultAudioEndpointManager.VerifyReadback(
                    defaultEndpointConfiguration, endpoints);

                if (defaultEndpointConfiguration.SystemPlaybackDefaultChanged)
                    break;

                Thread.Sleep(250);
            }
        }

        var defaultPlaybackReady = defaultEndpointConfiguration?.SystemPlaybackDefaultChanged == true;
        var status = windowsUpdateResult.RebootRequired
            ? speakerTest.TonePlayed && defaultPlaybackReady
                ? "Restart required: the built-in speakers currently pass the playback test and Windows accepted them as default, but Windows Update requires a restart to finalize the new driver."
                : "Restart required: Windows Update installed a matching audio driver and reports that Windows must restart before the device can be fully re-enumerated."
            : speakerTest.TonePlayed && defaultPlaybackReady
                ? "Ready: Windows accepted playback through the built-in speakers and they are now the system default output."
                : speakerTest.TonePlayed
                    ? "Playback verified on the built-in speakers, but Windows did not confirm changing the system default output. Some apps may still use their existing/per-app route."
                    : speakerTest.EndpointFound
                        ? "Partially configured: a built-in speaker endpoint exists, but test playback failed."
                        : "Not ready: no active built-in speaker endpoint is exposed by the current Windows audio driver stack.";

        var nextStep = windowsUpdateResult.RebootRequired
            ? "Restart Windows, run the installer again, and verify that the matching driver has completed installation and the built-in speakers are the system default output."
            : speakerTest.TonePlayed && defaultPlaybackReady
                ? "The built-in speaker endpoint passed the playback test and Windows accepted it as the Console and Multimedia default. The OEM kernel driver has not been replaced."
                : speakerTest.TonePlayed
                    ? "The built-in speaker endpoint played the test tone, but the Windows default-device change was not confirmed. Review DefaultPlaybackEndpoint role HRESULTs in the JSON report."
                    : "Check the report for PnP problem codes and missing platform dependencies. Add the laptop manufacturer's signed audio driver package to the Drivers folder and rerun the installer. A hardware-specific miniport/topology still has to exist for unsupported controllers/codecs.";

        return new AudioInstallationReport(
            system,
            devices.Count,
            assessments,
            endpoints,
            defaultEndpointConfiguration,
            serviceResults,
            packageResults,
            localDriverPnpRescanExitCode,
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
