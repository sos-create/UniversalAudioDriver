namespace UniversalAudioDriver.Models;

public sealed record WindowsUpdateDriverPackageResult(
    string Title,
    string HardwareId,
    string Provider,
    bool RequiresEula,
    int DownloadResultCode,
    int InstallResultCode,
    bool RebootRequired,
    string Status);

public sealed record WindowsUpdateDriverInstallResult(
    int AvailableDriverUpdates,
    int ExactHardwareMatches,
    int AcceptedPackages,
    bool DownloadSucceeded,
    bool InstallSucceeded,
    bool RebootRequired,
    string Status,
    IReadOnlyList<WindowsUpdateDriverPackageResult> Packages);
