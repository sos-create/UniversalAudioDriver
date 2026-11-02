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

        // Platform controllers must be staged before dependent DSP and codec
        // packages. This includes PCI, AMD ACP, Qualcomm, MediaTek and NXP hosts.
        if ((infClass.Equals("System", StringComparison.OrdinalIgnoreCase) &&
             (ids.Contains(@"PCI\") || ids.Contains("QCOM") ||
              ids.Contains("QUALCOMM") || ids.Contains("MTK") ||
              ids.Contains("MEDIATEK") || ids.Contains("NXP") ||
              ids.Contains("FREESCALE") || ids.Contains("IMX") ||
              ids.Contains("SAI"))) ||
            ids.Contains(@"PCI\VEN_1022&DEV_15E2") ||
            ids.Contains("AMD ACP") ||
            ids.Contains("AMD AUDIO COPROCESSOR"))
            return (0, "Platform controller");

        // DSP firmware and serial transport drivers precede codec functions.
        if (ids.Contains("SOUNDWIRE") || ids.Contains("SLIMBUS") ||
            ids.Contains("INTELAUDIO") || ids.Contains("INTEL SMART SOUND") ||
            ids.Contains("SMART SOUND TECHNOLOGY") || ids.Contains("SST") ||
            ids.Contains("SOUND OPEN FIRMWARE") || ids.Contains("SOF DSP") ||
            ids.Contains("ADSP") || ids.Contains("QCOM") ||
            ids.Contains("QUALCOMM ADSP") || ids.Contains("MTK AUDIO DSP") ||
            ids.Contains("I2S") || ids.Contains("SAI"))
            return (1, "Audio transport / DSP");

        if (ids.Contains("SDCA") || ids.Contains("HDAUDIO") ||
            ids.Contains("WCD") || ids.Contains("TFA98") || ids.Contains("TFA99") ||
            ids.Contains("ALC") || ids.Contains("RT56") || ids.Contains("RT101") ||
            ids.Contains("CS35") || ids.Contains("CS42") || ids.Contains("CS47") ||
            ids.Contains("CX") || ids.Contains("NAU88") || ids.Contains("ES83") ||
            ids.Contains("TAS") || ids.Contains("MAX98") || ids.Contains("DA721") ||
            infClass.Equals("MEDIA", StringComparison.OrdinalIgnoreCase))
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
        var qualcommController = Classify(
            "[Version]\r\nClass=System\r\n",
            new[] { @"ACPI\QCOM1234" });
        var qualcommDsp = Classify(
            "[Version]\r\nClass=MEDIA\r\n",
            new[] { @"ACPI\QCOM&DEV_ADSP" });
        var mediatekCodec = Classify(
            "[Version]\r\nClass=MEDIA\r\n",
            new[] { @"ACPI\MTK&DEV_MT6359" });
        var nxpAmp = Classify(
            "[Version]\r\nClass=MEDIA\r\n",
            new[] { @"ACPI\NXP&TFA9874" });
        var sofDsp = Classify(
            "[Version]\r\nClass=MEDIA\r\n",
            new[] { "Sound Open Firmware DSP" });

        if (controller.Order >= transport.Order ||
            transport.Order >= codec.Order ||
            codec.Order >= extension.Order ||
            !controller.Name.Contains("controller", StringComparison.OrdinalIgnoreCase) ||
            !extension.Name.Contains("extension", StringComparison.OrdinalIgnoreCase) ||
            qualcommController.Order != 0 ||
            qualcommDsp.Order != 1 ||
            mediatekCodec.Order != 2 ||
            nxpAmp.Order != 2 ||
            sofDsp.Order != 1)
        {
            throw new InvalidOperationException("Driver bundle dependency-order self-test failed.");
        }
    }
}
