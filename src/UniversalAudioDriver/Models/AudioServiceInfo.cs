namespace UniversalAudioDriver.Models;

public sealed record AudioServiceInfo(
    string Name,
    string DisplayName,
    string State,
    string StartMode);
