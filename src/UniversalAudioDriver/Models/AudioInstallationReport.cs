using UniversalAudioDriver.Engine;

namespace UniversalAudioDriver.Models;

public sealed record DriverPackageInstallResult(
    string InfPath,
    IReadOnlyList<string> MatchedDeviceIds,
    int ExitCode,
    string Status,
    string Output);

public sealed record AudioServiceRecoveryResult(
    string ServiceName,
    string StateBefore,
    string StateAfter,
    string Result);

public sealed record SpeakerTestResult(
    bool EndpointFound,
    string? EndpointName,
    string? EndpointId,
    bool TonePlayed,
    string Result);

public sealed record AudioInstallationReport(
    SystemHardwareInfo System,
    int DetectedAudioDeviceCount,
    IReadOnlyList<CompatibilityAssessment> HardwareAssessments,
    IReadOnlyList<AudioEndpointInfo> PlaybackEndpoints,
    DefaultAudioEndpointConfigurationResult? DefaultPlaybackEndpoint,
    IReadOnlyList<AudioServiceRecoveryResult> Services,
    IReadOnlyList<DriverPackageInstallResult> DriverPackages,
    WindowsUpdateDriverInstallResult WindowsUpdateDrivers,
    int PnpRescanExitCode,
    SpeakerTestResult SpeakerTest,
    string Status,
    string NextStep);
