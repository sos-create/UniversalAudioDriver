namespace UniversalAudioDriver.Engine;

/// <summary>
/// Hardware-name and PnP-ID catalog for common laptop audio controllers,
/// codecs and smart amplifiers. This catalog is used for identification only;
/// exact driver compatibility is still established by Windows INF matching.
/// </summary>
public static class AudioHardwareCatalog
{
    private static readonly (string Marker, string Name)[] Vendors =
    {
        (@"VEN_10EC", "Realtek"),
        (@"VEN_1013", "Cirrus Logic"),
        (@"VEN_101A", "Cirrus Logic"),
        (@"VEN_14F1", "Conexant"),
        (@"VEN_11D4", "Analog Devices"),
        (@"VEN_111D", "IDT"),
        (@"VEN_8384", "SigmaTel"),
        (@"VEN_125D", "ESS Technology"),
        (@"VEN_1106", "VIA"),
        (@"VEN_0D8C", "C-Media"),
        (@"VEN_13F6", "C-Media"),
        (@"VEN_1102", "Creative"),
        (@"VEN_10DE", "NVIDIA"),
        (@"VEN_1002", "AMD"),
        (@"VEN_1022", "AMD"),
        (@"VEN_8086", "Intel"),
        (@"VEN_14C3", "MediaTek"),
        (@"VEN_17CB", "Qualcomm"),
        (@"VEN_1FC9", "NXP"),
        (@"VEN_17AA", "Lenovo platform"),
        (@"VEN_103C", "HP platform"),
        (@"VEN_1025", "Acer platform"),
        (@"VEN_1043", "ASUS platform"),
        (@"VEN_144D", "Samsung platform"),
        ("CIRRUS", "Cirrus Logic"),
        ("REALTEK", "Realtek"),
        ("CONEXANT", "Conexant"),
        ("SYNAPTICS", "Synaptics"),
        ("NUVOTON", "Nuvoton"),
        ("WOLFSON", "Wolfson"),
        ("MAXIM", "Maxim Integrated"),
        ("ANALOG DEVICES", "Analog Devices"),
        ("TEXAS INSTRUMENTS", "Texas Instruments"),
        ("EVEREST", "Everest Semiconductor"),
        ("MEDIATEK", "MediaTek"),
        ("QUALCOMM", "Qualcomm"),
        ("NXP", "NXP"),
        ("ESS TECHNOLOGY", "ESS Technology"),
        ("SIGMATEL", "SigmaTel"),
        ("C-MEDIA", "C-Media"),
        ("CREATIVE", "Creative")
    };

    private static readonly (string Marker, string Name)[] Codecs =
    {
        ("ALC", "Realtek ALC series"),
        ("RT568", "Realtek RT56xx"),
        ("RT567", "Realtek RT56xx"),
        ("RT551", "Realtek RT55xx"),
        ("RT52", "Realtek RT52xx"),
        ("RT101", "Realtek RT101x smart amplifier"),
        ("CS42", "Cirrus Logic CS42xx codec"),
        ("CS35", "Cirrus Logic CS35xx smart amplifier"),
        ("CS47", "Cirrus Logic CS47xx codec"),
        ("CS44", "Cirrus Logic CS44xx codec"),
        ("CX807", "Conexant CX807x codec"),
        ("CX118", "Conexant CX118xx codec"),
        ("CX20", "Conexant CX20xx codec"),
        ("WCD", "Qualcomm WCD codec"),
        ("NAU88", "Nuvoton NAU88xx codec"),
        ("ES83", "Everest ES83xx codec"),
        ("ES85", "Everest ES85xx codec"),
        ("TAS25", "Texas Instruments TAS25xx smart amplifier"),
        ("TAS27", "Texas Instruments TAS27xx smart amplifier"),
        ("MAX983", "Maxim MAX983xx smart amplifier"),
        ("MAX98", "Maxim MAX98xx smart amplifier"),
        ("MAX97", "Maxim MAX97xx codec"),
        ("DA721", "Dialog Semiconductor DA721x codec"),
        ("DA739", "Dialog Semiconductor DA739x amplifier"),
        ("NAU", "Nuvoton audio codec"),
        ("SST", "Intel Smart Sound DSP"),
        ("ACP", "AMD Audio CoProcessor")
    };

    private static readonly string[] AmplifierMarkers =
    {
        "SMARTAMP", "SMART AMP", "AMPLIFIER", "AMP ",
        "MAX983", "MAX98", "CS35", "TAS25", "TAS27",
        "RT101", "DA739", "SMART AMP", "SMARTAMP"
    };

    private static readonly string[] DspMarkers =
    {
        "INTELAUDIO", "INTEL SMART SOUND", "SMART SOUND",
        "AMD ACP", "AUDIO COPROCESSOR", "DSP", "SST",
        "AUDIO PROCESSOR", "AUDIO DSP"
    };

    public static string DetectVendor(string signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        var value = signature.ToUpperInvariant();
        foreach (var (marker, name) in Vendors)
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return name;
        return "Unknown vendor";
    }

    public static string DetectCodecFamily(string signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        var value = signature.ToUpperInvariant();
        foreach (var (marker, name) in Codecs)
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return name;
        return "Codec model not identified";
    }

    public static string DetectComponentRole(string signature, string? pnpClass)
    {
        ArgumentNullException.ThrowIfNull(signature);
        var value = signature.ToUpperInvariant();

        if (AmplifierMarkers.Any(value.Contains))
            return "Speaker amplifier / Smart Amp";

        if (DspMarkers.Any(value.Contains))
            return "Audio controller / DSP";

        if (new[]
            {
                "HDAUDIO", "SOUNDWIRE", "SDCA", "SLIMBUS", "CODEC",
                "I2S", "I2C", "ALC", "RT568", "RT567", "RT551", "CS42",
                "CS47", "CX807", "CX118", "WCD", "NAU88", "ES83", "DA721"
            }.Any(value.Contains))
            return "Audio codec / serial audio component";

        if (string.Equals(pnpClass, "SoftwareComponent", StringComparison.OrdinalIgnoreCase) ||
            new[] { "DOLBY", "DTS", "NAHIMIC", "WAVES", "MAXX AUDIO", "DIRAC", "DTS:X" }
                .Any(value.Contains))
            return "Audio software / effects component";

        if (string.Equals(pnpClass, "AudioEndpoint", StringComparison.OrdinalIgnoreCase))
            return "Windows audio endpoint";

        if (string.Equals(pnpClass, "MEDIA", StringComparison.OrdinalIgnoreCase))
            return "Audio controller / media device";

        return "Audio platform dependency";
    }

    public static bool IsKnownCodecOrAmplifier(string signature) =>
        DetectCodecFamily(signature) != "Codec model not identified";

    public static void RunSelfTests()
    {
        AssertVendor(@"HDAUDIO\FUNC_01&VEN_10EC", "Realtek");
        AssertVendor(@"HDAUDIO\FUNC_01&VEN_1013", "Cirrus Logic");
        AssertVendor(@"HDAUDIO\FUNC_01&VEN_14F1", "Conexant");
        AssertVendor(@"HDAUDIO\FUNC_01&VEN_11D4", "Analog Devices");
        AssertVendor(@"HDAUDIO\FUNC_01&VEN_111D", "IDT");
        AssertVendor(@"HDAUDIO\FUNC_01&VEN_8384", "SigmaTel");
        AssertVendor(@"HDAUDIO\FUNC_01&VEN_125D", "ESS Technology");

        AssertCodec("Realtek ALC295", "Realtek ALC series");
        AssertCodec("RT5682", "Realtek RT56xx");
        AssertCodec("Cirrus CS42L42", "Cirrus Logic CS42xx codec");
        AssertCodec("CS35L41 Smart Amp", "Cirrus Logic CS35xx smart amplifier");
        AssertCodec("Conexant CX8070", "Conexant CX807x codec");
        AssertCodec("Qualcomm WCD9385", "Qualcomm WCD codec");
        AssertCodec("Nuvoton NAU88L25", "Nuvoton NAU88xx codec");
        AssertCodec("Everest ES8316", "Everest ES83xx codec");
        AssertCodec("TI TAS2563", "Texas Instruments TAS25xx smart amplifier");
        AssertCodec("Maxim MAX98357", "Maxim MAX983xx smart amplifier");
        AssertCodec("Dialog DA7219", "Dialog Semiconductor DA721x codec");

        if (DetectComponentRole("TAS2563 smart amplifier", "MEDIA") != "Speaker amplifier / Smart Amp" ||
            DetectComponentRole("Intel SST DSP", "System") != "Audio controller / DSP" ||
            DetectComponentRole("Realtek ALC295 HDAUDIO", "MEDIA") != "Audio codec / serial audio component")
            throw new InvalidOperationException("Audio hardware component role catalog self-test failed.");
    }

    private static void AssertVendor(string signature, string expected)
    {
        var actual = DetectVendor(signature);
        if (!actual.Equals(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Vendor catalog mismatch for '{signature}': '{actual}' != '{expected}'.");
    }

    private static void AssertCodec(string signature, string expected)
    {
        var actual = DetectCodecFamily(signature);
        if (!actual.Equals(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Codec catalog mismatch for '{signature}': '{actual}' != '{expected}'.");
    }
}
