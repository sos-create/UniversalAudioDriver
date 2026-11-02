using System.Management;
using UniversalAudioDriver.Models;

namespace UniversalAudioDriver.Engine;

/// <summary>
/// Starts only the two standard Windows audio services. It does not change their
/// startup type, registry configuration, or dependencies.
/// </summary>
public sealed class WindowsAudioServiceManager
{
    private static readonly string[] RequiredServices =
    {
        "AudioEndpointBuilder",
        "Audiosrv"
    };

    public IReadOnlyList<AudioServiceRecoveryResult> EnsureRunning()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows audio services are available only on Windows.");

        var results = new List<AudioServiceRecoveryResult>();
        foreach (var serviceName in RequiredServices)
            results.Add(EnsureServiceRunning(serviceName));

        return results;
    }

    private static AudioServiceRecoveryResult EnsureServiceRunning(string serviceName)
    {
        var before = ReadState(serviceName);
        if (string.Equals(before, "Running", StringComparison.OrdinalIgnoreCase))
            return new AudioServiceRecoveryResult(serviceName, before, before, "Already running");

        if (string.Equals(before, "Not found", StringComparison.OrdinalIgnoreCase))
            return new AudioServiceRecoveryResult(serviceName, before, before, "Service not found");

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                $"SELECT * FROM Win32_Service WHERE Name = '{serviceName}'");
            using var results = searcher.Get();

            foreach (ManagementObject service in results)
            {
                using (service)
                {
                    var startMode = service["StartMode"]?.ToString() ?? "Unknown";
                    if (string.Equals(startMode, "Disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        return new AudioServiceRecoveryResult(
                            serviceName, before, before,
                            "Disabled; startup configuration was not changed");
                    }

                    var returnValue = Convert.ToUInt32(
                        service.InvokeMethod("StartService", Array.Empty<object>()) ?? uint.MaxValue);

                    // Win32_Service.StartService: 0 = success, 10 = already running.
                    if (returnValue != 0 && returnValue != 10)
                    {
                        return new AudioServiceRecoveryResult(
                            serviceName, before, ReadState(serviceName),
                            $"StartService returned {returnValue}");
                    }

                    for (var attempt = 0; attempt < 10; attempt++)
                    {
                        var after = ReadState(serviceName);
                        if (string.Equals(after, "Running", StringComparison.OrdinalIgnoreCase))
                        {
                            return new AudioServiceRecoveryResult(
                                serviceName, before, after,
                                returnValue == 10 ? "Already running" : "Started successfully");
                        }

                        Thread.Sleep(300);
                    }

                    return new AudioServiceRecoveryResult(
                        serviceName, before, ReadState(serviceName),
                        "Start requested, but service did not reach Running state");
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new AudioServiceRecoveryResult(
                serviceName, before, ReadState(serviceName),
                $"Could not start service: {ex.Message}");
        }

        return new AudioServiceRecoveryResult(serviceName, before, "Not found", "Service not found");
    }

    private static string ReadState(string serviceName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                $"SELECT State FROM Win32_Service WHERE Name = '{serviceName}'");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                    return item["State"]?.ToString() ?? "Unknown";
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException)
        {
            return $"Unavailable: {ex.Message}";
        }

        return "Not found";
    }
}
