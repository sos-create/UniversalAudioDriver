namespace UniversalAudioDriver.Models;

public sealed record AudioDeviceInfo(
    string Name,
    string DeviceId,
    string? Manufacturer,
    string? PnpClass,
    string? Service,
    string? DriverProvider,
    string? DriverVersion,
    string? InfName,
    string Status,
    uint ProblemCode,
    string Family,
    IReadOnlyList<string> HardwareIds,
    IReadOnlyList<string> CompatibleIds,
    IReadOnlyList<string> ParentDeviceIds);
