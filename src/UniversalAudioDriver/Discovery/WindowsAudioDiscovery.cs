using System.Management;
using UniversalAudioDriver.Models;

namespace UniversalAudioDriver.Discovery;

public sealed class WindowsAudioDiscovery
{
    private static readonly HashSet<uint> RecoverableProblemCodes = new() { 10, 28, 31, 43 };

    private static readonly string[] AudioParentMarkers =
    {
        "AUDIO", "SOUND", "SST", "ACP", "HDAUDIO", "SDCA",
        "SOUNDWIRE", "SLIMBUS", "I2S", "CODEC", "DSP", "SMART SOUND"
    };

    private const string AudioFilter =
        "PNPClass = 'MEDIA' OR PNPClass = 'AudioEndpoint' OR " +
        "PNPClass = 'SoftwareComponent' OR Name LIKE '%Audio%' OR " +
        "Name LIKE '%Sound%' OR Name LIKE '%Speaker%' OR " +
        "Name LIKE '%Realtek%' OR Name LIKE '%Conexant%' OR " +
        "Name LIKE '%Cirrus%' OR Name LIKE '%Intel(R) Smart Sound%' OR " +
        "Name LIKE '%Dolby%' OR Name LIKE '%DTS%' OR Name LIKE '%Nahimic%' OR " +
        "DeviceID LIKE '%HDAUDIO%' OR DeviceID LIKE '%INTELAUDIO%' OR " +
        "DeviceID LIKE '%SDCA%'";

    private static readonly (string Id, string Name)[] PciVendors =
    {
        ("VEN_10EC", "Realtek"),
        ("VEN_8086", "Intel"),
        ("VEN_1022", "AMD"),
        ("VEN_1002", "AMD (ATI)"),
        ("VEN_10DE", "NVIDIA"),
        ("VEN_14F1", "Conexant"),
        ("VEN_1013", "Cirrus Logic"),
        ("VEN_101A", "Cirrus Logic"),
        ("VEN_1102", "Creative"),
        ("VEN_1106", "VIA"),
        ("VEN_0D8C", "C-Media"),
        ("VEN_125D", "ESS Technology"),
        ("VEN_11D4", "Analog Devices"),
        ("VEN_111D", "IDT"),
        ("VEN_8384", "SigmaTel"),
        ("VEN_13F6", "C-Media"),
        ("VEN_14C3", "MediaTek"),
        ("VEN_17CB", "Qualcomm"),
        ("VEN_1FC9", "NXP"),
        ("VEN_1043", "ASUS"),
        ("VEN_1025", "Acer"),
        ("VEN_17AA", "Lenovo"),
        ("VEN_103C", "HP"),
        ("VEN_144D", "Samsung"),
        ("VEN_1B21", "ASMedia")
    };

    public IReadOnlyList<AudioServiceInfo> ScanServices()
    {
        EnsureWindows();
        var services = new List<AudioServiceInfo>();
        using var searcher = new ManagementObjectSearcher(
            "root\\CIMV2",
            "SELECT Name, DisplayName, State, StartMode FROM Win32_Service " +
            "WHERE Name = 'Audiosrv' OR Name = 'AudioEndpointBuilder'");
        using var results = searcher.Get();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                services.Add(new AudioServiceInfo(
                    ReadString(item, "Name") ?? "Unknown",
                    ReadString(item, "DisplayName") ?? "Unknown",
                    ReadString(item, "State") ?? "Unknown",
                    ReadString(item, "StartMode") ?? "Unknown"));
            }
        }
        return services.OrderBy(s => s.Name).ToArray();
    }

    public IReadOnlyList<AudioDeviceInfo> Scan()
    {
        EnsureWindows();
        var devices = new List<AudioDeviceInfo>();
        var drivers = LoadDriverInventory();
        var pnpIdentifiers = LoadPnpIdentifierInventory();

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
                var compatibleIds = ReadStringArray(item, "CompatibleID");
                var name = ReadString(item, "Name") ?? "(unnamed device)";
                drivers.TryGetValue(id, out var driver);

                // Audio codecs and DSPs are often children of a bus/controller.
                // Keep the full PnP ancestry so later matching can use real IDs.
                var parentIds = PnpDeviceTopologyDiscovery.GetParentDeviceIds(id);
                var parentHardwareIds = new List<string>();
                var parentCompatibleIds = new List<string>();
                foreach (var parentId in parentIds)
                {
                    if (!pnpIdentifiers.TryGetValue(parentId, out var parentIdentifiers))
                        continue;

                    parentHardwareIds.AddRange(parentIdentifiers.HardwareIds);
                    parentCompatibleIds.AddRange(parentIdentifiers.CompatibleIds);
                }

                var distinctParentHardwareIds = parentHardwareIds
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var distinctParentCompatibleIds = parentCompatibleIds
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

                var allHardwareTokens = hardwareIds
                    .Concat(compatibleIds)
                    .Concat(distinctParentHardwareIds)
                    .Concat(distinctParentCompatibleIds)
                    .ToArray();

                var classification = Classify(
                    id, allHardwareTokens, name, driver.Provider, parentIds);

                devices.Add(new AudioDeviceInfo(
                    name,
                    id,
                    ReadString(item, "Manufacturer") ?? classification.Vendor,
                    ReadString(item, "PNPClass"),
                    ReadString(item, "Service"),
                    driver.Provider,
                    driver.Version,
                    driver.InfName,
                    ReadString(item, "Status") ?? "Unknown",
                    ReadUInt(item, "ConfigManagerErrorCode"),
                    classification.Family,
                    hardwareIds,
                    compatibleIds,
                    parentIds,
                    distinctParentHardwareIds,
                    distinctParentCompatibleIds));
            }
        }

        AddRecoverableAudioParentDevices(devices, pnpIdentifiers, drivers);

        return devices
            .GroupBy(d => d.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(d => d.PnpClass)
            .ThenBy(d => d.Name)
            .ToArray();
    }

    private sealed record PnpIdentifierSet(
        IReadOnlyList<string> HardwareIds,
        IReadOnlyList<string> CompatibleIds,
        string Name,
        string? Manufacturer,
        string? PnpClass,
        string? Service,
        string Status,
        uint ProblemCode);

    private static Dictionary<string, PnpIdentifierSet> LoadPnpIdentifierInventory()
    {
        var identifiers = new Dictionary<string, PnpIdentifierSet>(
            StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT PNPDeviceID, HardwareID, CompatibleID, Name, Manufacturer, " +
                "PNPClass, Service, Status, ConfigManagerErrorCode FROM Win32_PnPEntity");
            using var results = searcher.Get();

            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    var id = ReadString(item, "PNPDeviceID");
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    identifiers[id] = new PnpIdentifierSet(
                        ReadStringArray(item, "HardwareID"),
                        ReadStringArray(item, "CompatibleID"),
                        ReadString(item, "Name") ?? "(unnamed PnP device)",
                        ReadString(item, "Manufacturer"),
                        ReadString(item, "PNPClass"),
                        ReadString(item, "Service"),
                        ReadString(item, "Status") ?? "Unknown",
                        ReadUInt(item, "ConfigManagerErrorCode"));
                }
            }
        }
        catch (ManagementException)
        {
            // The parent instance IDs remain available when optional metadata cannot be read.
        }

        return identifiers;
    }

    private static void AddRecoverableAudioParentDevices(
        List<AudioDeviceInfo> devices,
        IReadOnlyDictionary<string, PnpIdentifierSet> pnpIdentifiers,
        IReadOnlyDictionary<string, (string? Provider, string? Version, string? InfName)> drivers)
    {
        var existingIds = devices
            .Select(device => device.DeviceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidateParentIds = devices
            .SelectMany(device => device.ParentDeviceIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var parentId in candidateParentIds)
        {
            if (existingIds.Contains(parentId) ||
                !pnpIdentifiers.TryGetValue(parentId, out var parent) ||
                !RecoverableProblemCodes.Contains(parent.ProblemCode) ||
                !LooksLikeAudioParent(parentId, parent))
            {
                continue;
            }

            var parentAncestors = PnpDeviceTopologyDiscovery.GetParentDeviceIds(parentId);
            var ancestorHardwareIds = new List<string>();
            var ancestorCompatibleIds = new List<string>();

            foreach (var ancestorId in parentAncestors)
            {
                if (!pnpIdentifiers.TryGetValue(ancestorId, out var ancestor))
                    continue;

                ancestorHardwareIds.AddRange(ancestor.HardwareIds);
                ancestorCompatibleIds.AddRange(ancestor.CompatibleIds);
            }

            var distinctAncestorHardwareIds = ancestorHardwareIds
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var distinctAncestorCompatibleIds = ancestorCompatibleIds
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            drivers.TryGetValue(parentId, out var driver);
            var signatureIds = parent.HardwareIds
                .Concat(parent.CompatibleIds)
                .Concat(distinctAncestorHardwareIds)
                .Concat(distinctAncestorCompatibleIds)
                .ToArray();

            var classification = Classify(
                parentId,
                signatureIds,
                parent.Name,
                driver.Provider,
                parentAncestors);

            devices.Add(new AudioDeviceInfo(
                parent.Name,
                parentId,
                parent.Manufacturer ?? classification.Vendor,
                parent.PnpClass,
                parent.Service,
                driver.Provider,
                driver.Version,
                driver.InfName,
                parent.Status,
                parent.ProblemCode,
                classification.Family,
                parent.HardwareIds,
                parent.CompatibleIds,
                parentAncestors,
                distinctAncestorHardwareIds,
                distinctAncestorCompatibleIds));

            existingIds.Add(parentId);
        }
    }

    private static bool LooksLikeAudioParent(string deviceId, PnpIdentifierSet device)
    {
        var signature = string.Join(" ",
            new[] { deviceId, device.Name }
                .Concat(device.HardwareIds)
                .Concat(device.CompatibleIds))
            .ToUpperInvariant();

        return AudioParentMarkers.Any(signature.Contains);
    }

    private static Dictionary<string, (string? Provider, string? Version, string? InfName)>
        LoadDriverInventory()
    {
        var drivers = new Dictionary<string, (string? Provider, string? Version, string? InfName)>(
            StringComparer.OrdinalIgnoreCase);

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
                    var id = ReadString(driver, "DeviceID");
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    drivers[id] = (
                        ReadString(driver, "DriverProviderName"),
                        ReadString(driver, "DriverVersion"),
                        ReadString(driver, "InfName"));
                }
            }
        }
        catch (ManagementException)
        {
            // Driver inventory is optional; core PnP discovery must still work.
        }

        return drivers;
    }

    private static (string Vendor, string Family) Classify(
        string deviceId,
        IReadOnlyList<string> ids,
        string? name,
        string? driverProvider,
        IReadOnlyList<string> parentDeviceIds)
    {
        var all = string.Join(" ", ids.Concat(parentDeviceIds)
            .Append(deviceId)
            .Append(name ?? "")
            .Append(driverProvider ?? "")).ToUpperInvariant();

        var vendor = PciVendors.FirstOrDefault(v => all.Contains(v.Id)).Name;
        if (string.IsNullOrWhiteSpace(vendor))
            vendor = DetectNamedVendor(all) ?? driverProvider ?? "Unknown vendor";

        var architecture = DetectArchitecture(all);
        return (vendor, $"{vendor} / {architecture}");
    }

    private static string? DetectNamedVendor(string value)
    {
        string[] vendors =
        {
            "REALTEK", "CONEXANT", "CIRRUS", "CREATIVE", "SOUND BLASTER",
            "C-MEDIA", "CMEDIA", "VIA", "ESS TECHNOLOGY", "ANALOG DEVICES",
            "SIGMATEL", "QUALCOMM", "MEDIATEK", "NVIDIA", "AMD", "INTEL",
            "DOLBY", "DTS", "NAHIMIC", "MAXX AUDIO", "WAVES"
        };

        return vendors.FirstOrDefault(value.Contains);
    }

    private static string DetectArchitecture(string value)
    {
        if (value.Contains("INTELAUDIO") || value.Contains("SMART SOUND") ||
            value.Contains("SST"))
            return "Intel Smart Sound Technology (SST)";
        if (value.Contains("AMD ACP") || value.Contains("AMD AUDIO") ||
            value.Contains("AUDIO COPROCESSOR"))
            return "AMD Audio CoProcessor (ACP)";
        if (value.Contains("SDCA"))
            return "SoundWire SDCA";
        if (value.Contains("SOUNDWIRE") || value.Contains("SLIMBUS"))
            return "SoundWire / SLIMbus";
        if (value.Contains("I2S") || value.Contains("I2C"))
            return "I2S / I2C platform audio";
        if (value.Contains("HDAUDIO") || value.Contains("HD AUDIO"))
            return "High Definition Audio (HDA) bus";
        if (value.Contains("BTHENUM") || value.Contains("BLUETOOTH") ||
            value.Contains("BTH\\"))
            return "Bluetooth audio";
        if (value.Contains("DISPLAY") || value.Contains("HDMI") ||
            value.Contains("DISPLAYPORT"))
            return "HDMI / DisplayPort digital audio";
        if (value.Contains("USB"))
            return "USB Audio Class";
        if (value.Contains("ACPI"))
            return "ACPI / platform-integrated audio";
        if (value.Contains("SWD") || value.Contains("SOFTWARECOMPONENT"))
            return "Windows software audio component / DSP";
        if (value.Contains("PCI"))
            return "PCI / PCI Express audio controller";
        return "Unclassified audio architecture";
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
