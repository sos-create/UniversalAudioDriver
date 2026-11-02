namespace UniversalAudioDriver.Models;

public sealed record AudioHardwareProfile(
    string Architecture,
    string DriverModel,
    string Confidence,
    IReadOnlyList<string> PlatformDependencies,
    string ImplementationStatus);
