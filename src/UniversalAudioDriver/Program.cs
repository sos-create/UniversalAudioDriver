using System.Text.Json;
using UniversalAudioDriver.Discovery;
using UniversalAudioDriver.Engine;

namespace UniversalAudioDriver;

internal static class Program
{
    private static string? GetOptionValue(string[] args, string option)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("Universal Audio Driver - hardware discovery and recovery");
        Console.WriteLine("Windows 10/11");
        Console.WriteLine();

        try
        {
            if (args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
            {
                AudioHardwareProfilePlanner.RunSelfTests();
                SignedDriverPackageInstaller.RunSelfTests();
                WindowsUpdateDriverInstaller.RunSelfTests();
                BuiltInSpeakerEndpointSelector.RunSelfTests();
                Console.WriteLine("Hardware profile, exact INF matching and speaker endpoint self-tests passed.");
                return 0;
            }

            if (args.Any(a => string.Equals(a, "--install", StringComparison.OrdinalIgnoreCase)))
            {
                var driverDirectory = GetOptionValue(args, "--driver-dir")
                    ?? Path.Combine(AppContext.BaseDirectory, "Drivers");
                var reportPath = GetOptionValue(args, "--report")
                    ?? Path.Combine(AppContext.BaseDirectory, "audio-install-report.json");

                Console.WriteLine("UniversalAudioDriver installation-time detection and configuration");
                Console.WriteLine($"Local signed driver package directory: {Path.GetFullPath(driverDirectory)}");
                Console.WriteLine("Only INF packages containing exact detected hardware IDs will be offered to Windows.");
                Console.WriteLine("Windows will enforce package signatures and driver ranking; no forced installation is used.");
                Console.WriteLine();

                var installReport = new WindowsAudioInstaller().Install(driverDirectory);
                var reportJson = JsonSerializer.Serialize(installReport,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(reportPath, reportJson);

                Console.WriteLine($"Computer: {installReport.System.Manufacturer} {installReport.System.Model}");
                Console.WriteLine(installReport.Status);
                foreach (var service in installReport.Services)
                    Console.WriteLine($"Service {service.ServiceName}: {service.StateBefore} -> {service.StateAfter} ({service.Result})");
                foreach (var package in installReport.DriverPackages)
                    Console.WriteLine($"INF {package.InfPath}: exit {package.ExitCode} ({package.Status})");
                if (installReport.LocalDriverPnpRescanExitCode is int localRescanCode)
                    Console.WriteLine($"PnP rescan after local OEM package: {localRescanCode}");
                Console.WriteLine($"Final PnP rescan exit code: {installReport.PnpRescanExitCode}");
                Console.WriteLine($"Windows Update drivers: {installReport.WindowsUpdateDrivers.Status}");
                foreach (var update in installReport.WindowsUpdateDrivers.Packages)
                    Console.WriteLine($"WU: {update.Title} [{update.HardwareId}] - {update.Status}");
                if (installReport.WindowsUpdateDrivers.RebootRequired)
                    Console.WriteLine("Windows Update reports that a restart is required to finish driver installation.");
                if (installReport.SpeakerTest.EndpointFound)
                    Console.WriteLine($"Built-in speaker endpoint: {installReport.SpeakerTest.EndpointName}");
                if (installReport.DefaultPlaybackEndpoint is { } defaultPlayback)
                {
                    Console.WriteLine($"Windows default playback: {defaultPlayback.Status}");
                    foreach (var role in defaultPlayback.RoleResults)
                        Console.WriteLine($"  {role.Role}: {role.Status}");
                }
                Console.WriteLine(installReport.SpeakerTest.Result);
                Console.WriteLine(installReport.NextStep);
                Console.WriteLine($"Detailed report: {Path.GetFullPath(reportPath)}");

                return installReport.SpeakerTest.TonePlayed &&
                       installReport.DefaultPlaybackEndpoint?.SystemPlaybackDefaultChanged == true
                    ? 0
                    : installReport.SpeakerTest.TonePlayed
                        ? 5
                        : installReport.SpeakerTest.EndpointFound
                            ? 3
                            : installReport.WindowsUpdateDrivers.RebootRequired ? 4 : 2;
            }

            if (args.Any(a => string.Equals(a, "--rescan-pnp", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("Requesting a Windows Plug and Play device rescan.");
                Console.WriteLine("Windows may ask for administrator permission.");
                Console.WriteLine("This does not force-install or replace any driver.");
                var exitCode = new WindowsPnpRecovery().RescanDevices();
                Console.WriteLine(exitCode == 0
                    ? "Windows Plug and Play rescan completed."
                    : $"PnPUtil returned exit code {exitCode}.");
                return exitCode;
            }

            if (args.Any(a => string.Equals(a, "--test-tone", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("Playing a short, low-volume test tone through the current default playback device...");
                new AudioPlaybackTest().PlayDefaultEndpointTone();
                Console.WriteLine("Test tone finished.");
                return 0;
            }

            if (args.Any(a => string.Equals(a, "--adaptive-test", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("Selecting an active Windows playback endpoint and adapting to its shared-mode format...");
                var result = new AdaptiveAudioOutput().PlayTestTone();
                Console.WriteLine($"Output: {result.EndpointName}");
                Console.WriteLine($"Sample format: {result.SampleRate} Hz, {result.Channels} channel(s)");
                Console.WriteLine($"Transport: {result.ShareMode}");
                Console.WriteLine($"Selection: {result.SelectionReason}");
                Console.WriteLine("Adaptive test tone finished.");
                return 0;
            }

            var systemInfo = new WindowsSystemDiscovery().Scan();
            Console.WriteLine($"Computer: {systemInfo.Manufacturer} {systemInfo.Model}");
            Console.WriteLine($"System type: {systemInfo.SystemType}");
            Console.WriteLine($"Processor: {systemInfo.Processor}");
            Console.WriteLine($"BIOS: {systemInfo.BiosVersion}");
            Console.WriteLine($"Windows: {systemInfo.WindowsVersion} (build {systemInfo.WindowsBuild})");
            Console.WriteLine();

            var discovery = new WindowsAudioDiscovery();
            var devices = discovery.Scan();
            var services = discovery.ScanServices();
            var endpoints = new CoreAudioEndpointDiscovery().ScanRenderEndpoints();
            var assessments = new AudioCompatibilityEngine().Assess(devices);

            if (assessments.Count == 0)
            {
                Console.WriteLine("No audio-related Plug and Play devices were found.");
            }
            else
            {
                foreach (var item in assessments)
                {
                    Console.WriteLine($"[{item.Result}] {item.DeviceName}");
                    Console.WriteLine($"  Family: {item.DetectedFamily}");
                    Console.WriteLine($"  Device ID: {item.DeviceId}");
                    Console.WriteLine($"  PnP class: {item.PnpClass ?? "unknown"}; status: {item.DeviceStatus}; problem code: {item.ProblemCode}");
                    Console.WriteLine($"  Driver: {item.DriverProvider ?? "unknown"} {item.DriverVersion ?? ""}; INF: {item.InfName ?? "unknown"}");
                    foreach (var hardwareId in item.HardwareIds)
                        Console.WriteLine($"  Hardware ID: {hardwareId}");
                    foreach (var compatibleId in item.CompatibleIds)
                        Console.WriteLine($"  Compatible ID: {compatibleId}");
                    Console.WriteLine($"  {item.Explanation}");
                    Console.WriteLine($"  Backend profile: {item.HardwareProfile.Architecture}");
                    Console.WriteLine($"  Driver model: {item.HardwareProfile.DriverModel}");
                    Console.WriteLine($"  Profile confidence: {item.HardwareProfile.Confidence}");
                    if (item.ParentDeviceIds.Count > 0)
                    {
                        Console.WriteLine("  PnP parent chain (device IDs):");
                        foreach (var parentId in item.ParentDeviceIds)
                            Console.WriteLine($"    -> {parentId}");
                    }
                    foreach (var check in item.NextChecks)
                        Console.WriteLine($"  - {check}");
                    Console.WriteLine();
                }
            }

            Console.WriteLine("Windows playback endpoints (Core Audio):");
            foreach (var endpoint in endpoints)
            {
                var volume = endpoint.MasterVolumePercent is null ? "unavailable" : $"{endpoint.MasterVolumePercent:0}%";
                var mute = endpoint.IsMuted is null ? "unknown" : endpoint.IsMuted.Value ? "muted" : "not muted";
                Console.WriteLine($"  [{endpoint.State}] {endpoint.Name}");
                Console.WriteLine($"    Default role: {endpoint.Role}; volume: {volume}; {mute}");
            }
            Console.WriteLine();

            Console.WriteLine("Windows audio services:");
            foreach (var service in services)
                Console.WriteLine($"  {service.DisplayName}: {service.State} (startup: {service.StartMode})");
            Console.WriteLine();

            var output = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "audio-discovery.json";
            var snapshot = new { System = systemInfo, Devices = assessments, PlaybackEndpoints = endpoints, Services = services };
            var json = JsonSerializer.Serialize(snapshot,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(output, json);

            Console.WriteLine($"Detected devices: {devices.Count}");
            Console.WriteLine($"Machine-readable results saved to: {Path.GetFullPath(output)}");
            Console.WriteLine("Discovery is read-only. Use --rescan-pnp to explicitly request a Windows PnP rescan.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Operation failed: {ex.Message}");
            return 1;
        }
    }
}
