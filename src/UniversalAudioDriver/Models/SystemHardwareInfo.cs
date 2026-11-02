namespace UniversalAudioDriver.Models;

public sealed record SystemHardwareInfo(
    string Manufacturer,
    string Model,
    string SystemType,
    string Processor,
    string BiosVersion,
    string WindowsVersion,
    string WindowsBuild);
