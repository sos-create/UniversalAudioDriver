using UniversalAudioDriver.Models;

namespace UniversalAudioDriver.Engine;

/// <summary>
/// Selects a hardware-specific driver strategy from the actual PnP identifiers.
/// It plans a backend; it does not claim that a matching kernel miniport exists.
/// </summary>
public static class AudioHardwareProfilePlanner
{
    public static AudioHardwareProfile Plan(AudioDeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var signature = string.Join(" ", new[] { device.DeviceId }
            .Concat(device.HardwareIds)
            .Concat(device.CompatibleIds)
            .Concat(device.ParentDeviceIds)
            .Concat(device.ParentHardwareIds)
            .Concat(device.ParentCompatibleIds)
            .Append(device.Name)
            .Append(device.Family))
            .ToUpperInvariant();

        if (signature.Contains("INTELAUDIO") ||
            signature.Contains("INTEL SMART SOUND") ||
            signature.Contains("SST"))
        {
            if (signature.Contains("SDCA"))
                return Create(
                    "Intel SST + SoundWire SDCA",
                    "Intel SST DSP miniport + SDCA codec/topology",
                    "High",
                    "Intel SST controller and DSP firmware",
                    "SDCA codec driver and endpoint topology",
                    "Laptop OEM ACPI/firmware routing",
                    "Hardware profile identified. A matching physical-speaker miniport/topology is not included yet.");

            if (signature.Contains("SOUNDWIRE"))
                return Create(
                    "Intel SST + SoundWire",
                    "Intel SST DSP miniport + SoundWire codec/topology",
                    "High",
                    "Intel SST controller and DSP firmware",
                    "SoundWire codec driver and link configuration",
                    "Laptop OEM ACPI/firmware routing",
                    "Hardware profile identified. A matching physical-speaker miniport/topology is not included yet.");

            return Create(
                "Intel Smart Sound Technology (SST)",
                "Intel SST DSP miniport",
                "High",
                "Intel SST controller driver",
                "Matching DSP firmware and IPC implementation",
                "Codec/amplifier topology and laptop OEM ACPI routing",
                "Hardware profile identified. A matching physical-speaker miniport/topology is not included yet.");
        }

        if (signature.Contains("AMD AUDIO COPROCESSOR") ||
            signature.Contains("AMD ACP"))
        {
            return Create(
                "AMD Audio CoProcessor (ACP)",
                "AMD ACP platform audio miniport",
                "Medium",
                "AMD ACP controller/platform driver",
                "I2S or SoundWire codec driver",
                "Laptop OEM ACPI tables, amplifier and DSP firmware",
                "Hardware profile identified. ACP hardware revision and codec-specific implementation are still required.");
        }

        if (signature.Contains("SDCA"))
            return Create(
                "SoundWire SDCA",
                "SoundWire controller + SDCA codec/topology",
                "Medium",
                "SoundWire controller/link driver",
                "SDCA codec driver",
                "OEM ACPI resources, clocking and amplifier routing",
                "Protocol family identified. Controller and codec-specific implementation are not included yet.");

        if (signature.Contains("SOUNDWIRE") || signature.Contains("SLIMBUS"))
            return Create(
                "SoundWire / SLIMbus",
                "Serial-bus audio controller and codec miniports",
                "Medium",
                "SoundWire/SLIMbus controller driver",
                "Codec-specific firmware and topology",
                "OEM ACPI resources and amplifier routing",
                "Serial audio bus identified. Exact controller and codec support is still required.");

        if (signature.Contains("HDAUDIO") || signature.Contains("HD AUDIO"))
            return Create(
                "High Definition Audio (HDA)",
                "HDA bus + codec-specific PortCls miniport/topology",
                "High",
                "HDA bus/controller driver",
                "Codec verb, pin-widget and amplifier route configuration",
                "OEM codec coefficients, jack mapping and speaker topology",
                "HDA path identified. A generic codec-independent speaker miniport is not available in this project.");

        if (signature.Contains("USB"))
            return Create(
                "USB Audio Class",
                "Windows USB Audio class stack",
                "Medium",
                "USB host controller and compatible USB Audio Class device",
                "Device descriptors and supported audio formats",
                "This profile does not establish a route to built-in laptop speakers",
                "USB audio strategy identified; it is not a built-in-speaker driver.");

        if (signature.Contains("BTHENUM") || signature.Contains("BLUETOOTH"))
            return Create(
                "Bluetooth audio",
                "Windows Bluetooth audio profile stack",
                "Medium",
                "Bluetooth radio and Windows Bluetooth services",
                "Compatible A2DP/HFP profile",
                "This profile does not establish a route to built-in laptop speakers",
                "Bluetooth strategy identified; it is not a built-in-speaker driver.");

        return Create(
            "Unknown / platform-specific audio",
            "No backend selected",
            "Low",
            "Exact controller and codec hardware IDs",
            "Parent PnP chain, firmware interfaces and amplifier identification",
            "Laptop model and OEM audio topology",
            "Insufficient hardware evidence. No driver will be selected or installed automatically.");
    }

    /// <summary>
    /// Fast deterministic checks for CI; no physical audio device is modified.
    /// </summary>
    public static void RunSelfTests()
    {
        AssertArchitecture(
            CreateTestDevice(@"INTELAUDIO\FUNC_01&VEN_10EC", Array.Empty<string>()),
            "Intel Smart Sound Technology (SST)");
        AssertArchitecture(
            CreateTestDevice(@"PCI\VEN_1022&DEV_15E3", new[] { "AMD Audio CoProcessor (ACP)" }),
            "AMD Audio CoProcessor (ACP)");
        AssertArchitecture(
            CreateTestDevice(@"HDAUDIO\FUNC_01&VEN_10EC", Array.Empty<string>()),
            "High Definition Audio (HDA)");
        AssertArchitecture(
            CreateTestDevice(@"SDCA\01", Array.Empty<string>()),
            "SoundWire SDCA");
        AssertArchitecture(
            CreateTestDevice(@"HDAUDIO\FUNC_01&VEN_10EC", new[] { @"ACPI\VEN_INTEL&DEV_0001" }),
            "High Definition Audio (HDA)");
        AssertArchitecture(
            CreateTestDevice(@"USB\VID_1234&PID_5678", Array.Empty<string>()),
            "USB Audio Class");
    }

    private static AudioDeviceInfo CreateTestDevice(string deviceId, IReadOnlyList<string> parents) =>
        new(
            "Synthetic test device", deviceId, null, "MEDIA", null, null, null, null,
            "OK", 0, "Test family", new[] { deviceId }, Array.Empty<string>(),
            parents, Array.Empty<string>(), Array.Empty<string>());

    private static void AssertArchitecture(AudioDeviceInfo device, string expected)
    {
        var actual = Plan(device).Architecture;
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Audio profile self-test failed. Expected '{expected}', received '{actual}'.");
    }

    private static AudioHardwareProfile Create(
        string architecture,
        string driverModel,
        string confidence,
        params string[] dependenciesAndStatus)
    {
        if (dependenciesAndStatus.Length < 2)
            throw new ArgumentException("A profile requires dependencies and an implementation status.");

        return new AudioHardwareProfile(
            architecture,
            driverModel,
            confidence,
            dependenciesAndStatus[..^1],
            dependenciesAndStatus[^1]);
    }
}
