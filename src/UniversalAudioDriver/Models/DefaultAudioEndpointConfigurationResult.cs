namespace UniversalAudioDriver.Models;

public sealed record AudioEndpointRoleChange(
    string Role,
    int HResult,
    bool Succeeded,
    string Status);

public sealed record DefaultAudioEndpointConfigurationResult(
    bool Attempted,
    string EndpointName,
    string EndpointId,
    bool ConsoleChanged,
    bool MultimediaChanged,
    bool CommunicationsChanged,
    string Status,
    IReadOnlyList<AudioEndpointRoleChange> RoleResults)
{
    public bool SystemPlaybackDefaultChanged => ConsoleChanged && MultimediaChanged;
}
