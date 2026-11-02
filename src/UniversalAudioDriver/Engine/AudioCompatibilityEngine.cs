using UniversalAudioDriver.Models;

namespace UniversalAudioDriver.Engine;

public sealed record CompatibilityAssessment(
    string DeviceName,
    string DetectedFamily,
    string Result,
    string Explanation,
    string DeviceId,
    string? Manufacturer,
    string? PnpClass,
    string? Service,
    string? DriverProvider,
    string? DriverVersion,
    string? InfName,
    string DeviceStatus,
    uint ProblemCode,
    IReadOnlyList<string> HardwareIds,
    IReadOnlyList<string> CompatibleIds,
    IReadOnlyList<string> ParentDeviceIds,
    AudioHardwareProfile HardwareProfile,
    IReadOnlyList<string> NextChecks);

public sealed class AudioCompatibilityEngine
{
    public IReadOnlyList<CompatibilityAssessment> Assess(IEnumerable<AudioDeviceInfo> devices)
    {
        return devices.Select(device =>
        {
            var checks = new List<string>
            {
                "Check device status and Windows Plug and Play problem code.",
                "Check that the required Windows audio services are running.",
                "Match the complete hardware ID, not only the displayed vendor name."
            };

            var (result, explanation) = device.ProblemCode switch
            {
                0 when device.DriverVersion is not null =>
                    ("Detected", "Windows reports no Plug and Play problem and a signed driver is registered."),
                0 =>
                    ("Needs driver verification", "No Plug and Play problem is reported, but signed-driver metadata was not found."),
                28 =>
                    ("Driver missing", "Windows reports that no driver is installed for this device."),
                10 =>
                    ("Device failed to start", "The device could not start. A matching bus, codec, DSP or firmware component may be missing."),
                22 =>
                    ("Device disabled", "The device is disabled in Plug and Play."),
                31 =>
                    ("Driver load failure", "Windows could not load the required driver."),
                43 =>
                    ("Device reported a problem", "The device or its driver reported a failure."),
                _ =>
                    ("Needs investigation", $"Windows Plug and Play problem code: {device.ProblemCode}.")
            };

            if (device.ParentDeviceIds.Count == 0)
            {
                checks.Add("The Windows PnP parent chain was unavailable; inspect the device in Device Manager.");
            }
            else
            {
                checks.Add($"PnP ancestry collected ({device.ParentDeviceIds.Count} parent device(s)); use these IDs to match bus and platform drivers.");
            }

            var profile = AudioHardwareProfilePlanner.Plan(device);
            checks.AddRange(profile.PlatformDependencies.Select(dependency =>
                $"Required dependency: {dependency}"));
            checks.Add(profile.ImplementationStatus);
            checks.Add("Do not force-install a driver based only on a vendor name; a physical speaker path needs a matching controller, codec, amplifier and platform topology.");

            return new CompatibilityAssessment(
                device.Name, device.Family, result, explanation,
                device.DeviceId, device.Manufacturer, device.PnpClass,
                device.Service, device.DriverProvider, device.DriverVersion,
                device.InfName, device.Status, device.ProblemCode,
                device.HardwareIds, device.CompatibleIds,
                device.ParentDeviceIds, profile, checks);
        }).ToArray();
    }
}
