using System.Text.Json;
using UniversalAudioDriver.Discovery;
using UniversalAudioDriver.Engine;

namespace UniversalAudioDriver;

internal static class Program
{
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
                Console.WriteLine("Hardware profile planner self-tests passed.");
                return 0;
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
            var snapshot = new { Devices = assessments, PlaybackEndpoints = endpoints, Services = services };
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
