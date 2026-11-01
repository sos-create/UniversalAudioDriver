using System.Text.Json;
using UniversalAudioDriver.Discovery;
using UniversalAudioDriver.Engine;

namespace UniversalAudioDriver;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("Universal Audio Driver - hardware discovery");
        Console.WriteLine("Windows 10/11 | read-only scan");
        Console.WriteLine();

        try
        {
            var discovery = new WindowsAudioDiscovery();
            var devices = discovery.Scan();
            var services = discovery.ScanServices();
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
                    foreach (var check in item.NextChecks)
                        Console.WriteLine($"  - {check}");
                    Console.WriteLine();
                }
            }

            Console.WriteLine("Windows audio services:");
            foreach (var service in services)
                Console.WriteLine($"  {service.DisplayName}: {service.State} (startup: {service.StartMode})");
            Console.WriteLine();

            var output = args.Length > 0 ? args[0] : "audio-discovery.json";
            var snapshot = new { Devices = assessments, Services = services };
            var json = JsonSerializer.Serialize(snapshot,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(output, json);

            Console.WriteLine($"Detected devices: {devices.Count}");
            Console.WriteLine($"Machine-readable results saved to: {Path.GetFullPath(output)}");
            Console.WriteLine("No drivers, services, registry entries or device settings were changed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Discovery failed: {ex.Message}");
            return 1;
        }
    }
}
