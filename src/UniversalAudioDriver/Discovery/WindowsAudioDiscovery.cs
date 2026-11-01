using System.Management;
using UniversalAudioDriver.Models;

namespace UniversalAudioDriver.Discovery;

public sealed class WindowsAudioDiscovery
{
    private const string AudioFilter =
        "PNPClass = 'MEDIA' OR PNPClass = 'AudioEndpoint' OR " +
        "PNPClass = 'SoftwareComponent' OR Name LIKE '%Audio%' OR " +
        "Name LIKE '%Sound%' OR Name LIKE '%Speaker%' OR " +
        "Name LIKE '%Realtek%' OR Name LIKE '%Conexant%' OR " +
        "Name LIKE '%Cirrus%' OR Name LIKE '%Intel(R) Smart Sound%'";

    public IReadOnlyList<AudioDeviceInfo> Scan()
    {
        EnsureWindows();
        var devices = new List<AudioDeviceInfo>();

        using var searcher = new ManagementObjectSearcher(
            "root\\CIMV2",
            $"SELECT * FROM Win32_PnPEntity WHERE {AudioFilter}");

        using var results = searcher.Get();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                var id = ReadString(item, "PNPDeviceID") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var hardwareIds = ReadStringArray(item, "HardwareID");
                var driver = FindDriver(id);

                devices.Add(new AudioDeviceInfo(
                    ReadString(item, "Name") ?? "(unnamed device)",
                    id,
                    ReadString(item, "Manufacturer"),
                    ReadString(item, "PNPClass"),
                    ReadString(item, "Service"),
                    driver.Provider,
                    driver.Version,
                    driver.InfName,
                    ReadString(item, "Status") ?? "Unknown",
                    ReadUInt(item, "ConfigManagerErrorCode"),
                    Classify(id, hardwareIds, ReadString(item, "Name")),
                    hardwareIds));
            }
        }

        return devices
            .OrderBy(d => d.PnpClass)
            .ThenBy(d => d.Name)
            .ToArray();
    }

    private static (string? Provider, string? Version, string? InfName) FindDriver(string deviceId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT DeviceID, DriverProviderName, DriverVersion, InfName " +
                "FROM Win32_PnPSignedDriver");

            using var results = searcher.Get();
            foreach (ManagementObject driver in results)
            {
                using (driver)
                {
                    if (!string.Equals(ReadString(driver, "DeviceID"), deviceId,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    return (
                        ReadString(driver, "DriverProviderName"),
                        ReadString(driver, "DriverVersion"),
                        ReadString(driver, "InfName"));
                }
            }
        }
        catch (ManagementException)
        {
            // Missing optional driver inventory must not stop hardware discovery.
        }

        return (null, null, null);
    }

    private static string Classify(string deviceId, IReadOnlyList<string> ids, string? name)
    {
        var all = string.Join(" ", ids.Append(deviceId).Append(name ?? "")).ToUpperInvariant();

        if (all.Contains("INTELAUDIO") || all.Contains("SMART SOUND") ||
            all.Contains("VEN_8086") && all.Contains("DEV_"))
            return "Intel audio / SST candidate";
        if (all.Contains("HDAUDIO") || all.Contains("VEN_10EC"))
            return "HD Audio / Realtek candidate";
        if (all.Contains("VEN_14F1") || all.Contains("CONEXANT"))
            return "Conexant candidate";
        if (all.Contains("VEN_1013") || all.Contains("CIRRUS"))
            return "Cirrus Logic candidate";
        if (all.Contains("USB"))
            return "USB audio";
        if (all.Contains("BTH") || all.Contains("BLUETOOTH"))
            return "Bluetooth audio";
        if (all.Contains("ACPI"))
            return "ACPI audio-related device";

        return "Unclassified audio-related device";
    }

    private static string? ReadString(ManagementBaseObject item, string property) =>
        item[property]?.ToString();

    private static uint ReadUInt(ManagementBaseObject item, string property)
    {
        try { return Convert.ToUInt32(item[property] ?? 0); }
        catch { return 0; }
    }

    private static IReadOnlyList<string> ReadStringArray(ManagementBaseObject item, string property)
    {
        if (item[property] is not string[] values)
            return Array.Empty<string>();

        return values.Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("UniversalAudioDriver currently targets Windows 10/11.");
    }
}
