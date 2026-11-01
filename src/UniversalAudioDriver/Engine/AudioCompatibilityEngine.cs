using UniversalAudioDriver.Models;

namespace UniversalAudioDriver.Engine;

public sealed record CompatibilityAssessment(
    string DeviceName,
    string DetectedFamily,
    string Result,
    string Explanation,
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
                "Check the matching hardware ID before selecting any driver package."
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
                    ("Device failed to start", "Windows reports that the device could not start. A matching bus, codec or firmware component may be missing."),
                22 =>
                    ("Device disabled", "The device is disabled in Plug and Play."),
                31 =>
                    ("Driver load failure", "Windows could not load the required driver."),
                43 =>
                    ("Device reported a problem", "The device or its driver reported a failure."),
                _ =>
                    ("Needs investigation", $"Windows Plug and Play problem code: {device.ProblemCode}.")
            };

            if (device.Family.Contains("candidate", StringComparison.OrdinalIgnoreCase) ||
                device.Family.Contains("SST", StringComparison.OrdinalIgnoreCase))
                checks.Add("Resolve the exact codec/controller hardware ID and its parent-device chain.");

            checks.Add("Do not force-install a driver based only on a vendor name; match the complete hardware ID and platform dependencies.");

            return new CompatibilityAssessment(
                device.Name, device.Family, result, explanation, checks);
        }).ToArray();
    }
}
