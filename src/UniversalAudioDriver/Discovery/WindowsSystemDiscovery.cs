using System.Management;
using UniversalAudioDriver.Models;

namespace UniversalAudioDriver.Discovery;

/// <summary>
/// Reads non-unique platform information required to match OEM audio packages.
/// Serial numbers and other unique identifiers are intentionally not collected.
/// </summary>
public sealed class WindowsSystemDiscovery
{
    public SystemHardwareInfo Scan()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("System hardware discovery is available only on Windows.");

        var manufacturer = "Unknown";
        var model = "Unknown";
        var systemType = "Unknown";
        var processor = "Unknown";
        var biosVersion = "Unknown";

        using (var searcher = new ManagementObjectSearcher(
                   "root\\CIMV2",
                   "SELECT Manufacturer, Model, SystemType FROM Win32_ComputerSystem"))
        using (var results = searcher.Get())
        {
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    manufacturer = Read(item, "Manufacturer") ?? manufacturer;
                    model = Read(item, "Model") ?? model;
                    systemType = Read(item, "SystemType") ?? systemType;
                    break;
                }
            }
        }

        using (var searcher = new ManagementObjectSearcher(
                   "root\\CIMV2",
                   "SELECT Name FROM Win32_Processor"))
        using (var results = searcher.Get())
        {
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    processor = Read(item, "Name") ?? processor;
                    break;
                }
            }
        }

        using (var searcher = new ManagementObjectSearcher(
                   "root\\CIMV2",
                   "SELECT SMBIOSBIOSVersion, Version FROM Win32_BIOS"))
        using (var results = searcher.Get())
        {
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    biosVersion = Read(item, "SMBIOSBIOSVersion")
                        ?? Read(item, "Version")
                        ?? biosVersion;
                    break;
                }
            }
        }

        var osVersion = Environment.OSVersion.Version;
        return new SystemHardwareInfo(
            manufacturer,
            model,
            systemType,
            processor,
            biosVersion,
            Environment.OSVersion.VersionString,
            $"{osVersion.Major}.{osVersion.Minor}.{osVersion.Build}");
    }

    private static string? Read(ManagementBaseObject item, string property) =>
        item[property]?.ToString();
}
