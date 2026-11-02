namespace UniversalAudioDriver.Models;

public sealed record AudioEndpointInfo(
    string Name,
    string Id,
    string State,
    string Role,
    bool IsDefault,
    bool? IsMuted,
    float? MasterVolumePercent,
    string FormFactor,
    string? ControllerDeviceId);
