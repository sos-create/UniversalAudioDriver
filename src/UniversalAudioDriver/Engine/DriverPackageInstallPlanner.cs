namespace UniversalAudioDriver.Engine;

/// <summary>
/// Orders matching OEM INF packages by the dependency layer they normally serve.
/// Windows PnPUtil still validates applicability, signatures and driver rank.
/// This planner never manufactures a SYS binary or invents a device topology.
/// </summary>
public static class DriverPackageInstallPlanner
{
    public static (int Order, string Name) Classify(
        string infContents,
        IReadOnlyCollection<string> matchedHardwareIds)
    {
        ArgumentNullException.ThrowIfNull(infContents);
        ArgumentNullException.ThrowIfNull(matchedHardwareIds);

        var infClass = ReadVersionClass(infContents);
        var ids = string.Join(" ", matchedHardwareIds).ToUpperInvariant();

        // Extension and software-component packages are applied after their
        // controller/codec base drivers so their OEM property overrides land last.
        if (infClass.Equals("Extension", StringComparison.OrdinalIgnoreCase) ||
            infClass.Equals("SoftwareComponent", StringComparison.OrdinalIgnoreCase) ||
            ids.Contains(@"SWC\"))
            return (4, "OEM extension / software component");

        // Parent PCI controllers and the AMD ACP platform device must be staged
        // before serial-bus codecs and HDAudio function devices.
        if ((infClass.Equals("System", StringComparison.OrdinalIgnoreCase) &&
             ids.Contains(@"PCI\")) ||
            ids.Contains(@"PCI\VEN_1022&DEV_15E2") ||
            ids.Contains("AMD ACP") ||
            ids.Contains("AMD AUDIO COPROCESSOR"))
            return (0, "Platform controller");

        if (ids.Contains("SOUNDWIRE") || ids.Contains("SLIMBUS"))
            return (1, "Audio transport / DSP bus");

        if (ids.Contains("INTELAUDIO") || ids.Contains("INTEL SMART SOUND") ||
            ids.Contains("SMART SOUND TECHNOLOGY") || ids.Contains("SST"))
            return (1, "Intel Smart Sound / DSP");

        if (ids.Contains("SDCA") || ids.Contains("HDAUDIO") ||
            ids.Contains("I2S") || infClass.Equals("MEDIA", StringComparison.OrdinalIgnoreCase))
            return (2, "Codec / amplifier / audio function");

        return (3, "Other matched audio component");
    }

    private static string ReadVersionClass(string infContents)
    {
        var inVersion = false;
        foreach (var rawLine in infContents.Split(new[] { '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inVersion = line.Equals("[Version]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inVersion)
                continue;

            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            if (line[..separator].Trim().Equals("Class", StringComparison.OrdinalIgnoreCase))
                return line[(separator + 1)..].Trim().Trim('"');
        }

        return string.Empty;
    }

    public static void RunSelfTests()
    {
        var controller = Classify(
            "[Version]\r\nClass=System\r\n[Models]\r\n",
            new[] { @"PCI\VEN_8086&DEV_9D70" });
        var transport = Classify(
            "[Version]\r\nClass=MEDIA\r\n",
            new[] { @"SOUNDWIRE\01" });
        var codec = Classify(
            "[Version]\r\nClass=MEDIA\r\n",
            new[] { @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0256" });
        var extension = Classify(
            "[Version]\r\nClass=Extension\r\n",
            new[] { @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0256" });

        if (controller.Order >= transport.Order ||
            transport.Order >= codec.Order ||
            codec.Order >= extension.Order ||
            !controller.Name.Contains("controller", StringComparison.OrdinalIgnoreCase) ||
            !extension.Name.Contains("extension", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Driver bundle dependency-order self-test failed.");
        }
    }
}
