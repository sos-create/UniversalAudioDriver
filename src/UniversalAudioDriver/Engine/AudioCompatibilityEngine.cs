using UniversalAudioDriver.Models;

namespace UniversalAudioDriver.Engine;

public sealed record CompatibilityAssessment(
    string DeviceName,
    string DetectedFamily,
    string ComponentRole,
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
    IReadOnlyList<string> ParentHardwareIds,
    IReadOnlyList<string> ParentCompatibleIds,
    AudioHardwareProfile HardwareProfile,
    IReadOnlyList<string> NextChecks);

public sealed class AudioCompatibilityEngine
{
    public static string DetectComponentRole(AudioDeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var signature = string.Join(" ",
            new[] { device.Name, device.DeviceId, device.Family }
                .Concat(device.HardwareIds)
                .Concat(device.CompatibleIds)
                .Concat(device.ParentHardwareIds)
                .Concat(device.ParentCompatibleIds));

        return AudioHardwareCatalog.DetectComponentRole(signature, device.PnpClass);
    }

    public static void RunSelfTests()
    {
        var amplifier = CreateTestDevice(
            "Cirrus Logic CS35L41 SmartAMP", @"ACPI\\VEN_CIRR&DEV_35A1",
            "SoftwareComponent", @"ACPI\\VEN_CIRR&DEV_35A1");
        var dsp = CreateTestDevice(
            "Intel Smart Sound Technology DSP", @"INTELAUDIO\\DSP",
            "System", @"INTELAUDIO\\DSP");
        var codec = CreateTestDevice(
            "Realtek HDAUDIO codec", @"HDAUDIO\\FUNC_01&VEN_10EC",
            "MEDIA", @"HDAUDIO\\FUNC_01&VEN_10EC");
        var effects = CreateTestDevice(
            "Dolby Audio Processing Object", @"SWC\\DOLBY",
            "SoftwareComponent", @"SWC\\DOLBY");
        var acpiRoot = CreateTestDevice(
            "Generic ACPI Root Device", @"ACPI\\VEN_TEST&DEV_0001",
            "System", @"ACPI\\VEN_TEST&DEV_0001");
        var amdAcp = CreateTestDevice(
            "PCI Audio Controller", @"PCI\\VEN_1022&DEV_15E2",
            "System", @"PCI\\VEN_1022&DEV_15E2");

        if (DetectComponentRole(amplifier) != "Speaker amplifier / Smart Amp" ||
            DetectComponentRole(dsp) != "Audio controller / DSP" ||
            DetectComponentRole(codec) != "Audio codec / serial audio component" ||
            DetectComponentRole(effects) != "Audio software / effects component" ||
            DetectComponentRole(acpiRoot) != "Audio platform dependency" ||
            DetectComponentRole(amdAcp) != "Audio controller / DSP")
        {
            throw new InvalidOperationException("Audio component role detection self-test failed.");
        }
    }

    private static AudioDeviceInfo CreateTestDevice(
        string name,
        string deviceId,
        string? pnpClass,
        params string[] hardwareIds) =>
        new(
            name, deviceId, null, pnpClass, null, null, null, null,
            "OK", 0, "Synthetic audio family",
            hardwareIds, Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<string>());

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
            else if (device.ParentHardwareIds.Count > 0 || device.ParentCompatibleIds.Count > 0)
            {
                checks.Add($"PnP ancestry collected ({device.ParentDeviceIds.Count} parent device(s)) with {device.ParentHardwareIds.Count} parent hardware ID(s) and {device.ParentCompatibleIds.Count} parent compatible ID(s).");
            }
            else
            {
                checks.Add($"PnP ancestry collected ({device.ParentDeviceIds.Count} parent device(s)), but Windows did not return their hardware IDs; only instance topology is available for these parents.");
            }

            var profile = AudioHardwareProfilePlanner.Plan(device);
            var componentRole = DetectComponentRole(device);
            checks.AddRange(profile.PlatformDependencies.Select(dependency =>
                $"Required dependency: {dependency}"));
            checks.Add(profile.ImplementationStatus);
            checks.Add("Do not force-install a driver based only on a vendor name; a physical speaker path needs a matching controller, codec, amplifier and platform topology.");

            return new CompatibilityAssessment(
                device.Name, device.Family, componentRole, result, explanation,
                device.DeviceId, device.Manufacturer, device.PnpClass,
                device.Service, device.DriverProvider, device.DriverVersion,
                device.InfName, device.Status, device.ProblemCode,
                device.HardwareIds, device.CompatibleIds,
                device.ParentDeviceIds, device.ParentHardwareIds,
                device.ParentCompatibleIds, profile, checks);
        }).ToArray();
    }
}
