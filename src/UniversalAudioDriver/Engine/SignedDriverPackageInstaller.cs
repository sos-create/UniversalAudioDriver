using System.Diagnostics;
using System.Text;
using UniversalAudioDriver.Models;

namespace UniversalAudioDriver.Engine;

/// <summary>
/// Finds local INF packages that explicitly contain one of the device's exact
/// PnP hardware IDs. PnPUtil performs Windows signature and driver-rank checks.
/// The installer never uses /force and never disables signature enforcement.
/// </summary>
public sealed class SignedDriverPackageInstaller
{
    private static readonly HashSet<uint> RecoverableProblemCodes = new() { 10, 28, 31, 43 };
    private const int MaxInfFileSizeBytes = 10 * 1024 * 1024;

    public IReadOnlyList<DriverPackageInstallResult> InstallMatchingPackages(
        string driverDirectory,
        IReadOnlyList<AudioDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("PnPUtil driver installation is available only on Windows.");

        if (string.IsNullOrWhiteSpace(driverDirectory) || !Directory.Exists(driverDirectory))
            return Array.Empty<DriverPackageInstallResult>();

        var targets = devices
            .Where(device => RecoverableProblemCodes.Contains(device.ProblemCode))
            .Where(device => !string.Equals(device.PnpClass, "AudioEndpoint", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (targets.Length == 0)
            return Array.Empty<DriverPackageInstallResult>();

        var candidates = FindMatchingInfPackages(driverDirectory, targets);
        var results = new List<DriverPackageInstallResult>();

        foreach (var candidate in candidates)
            results.Add(InstallPackage(candidate.Path, candidate.DeviceIds));

        return results;
    }

    private static IReadOnlyList<InfCandidate> FindMatchingInfPackages(
        string driverDirectory,
        IReadOnlyList<AudioDeviceInfo> targetDevices)
    {
        var matches = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> infFiles;
        try
        {
            infFiles = Directory.EnumerateFiles(
                driverDirectory, "*.inf", SearchOption.AllDirectories).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<InfCandidate>();
        }

        foreach (var infPath in infFiles)
        {
            try
            {
                var info = new FileInfo(infPath);
                if (info.Length <= 0 || info.Length > MaxInfFileSizeBytes)
                    continue;

                var contents = File.ReadAllText(infPath);
                foreach (var device in targetDevices)
                {
                    var exactIds = device.HardwareIds
                        .Concat(device.CompatibleIds)
                        .Concat(device.ParentHardwareIds)
                        .Concat(device.ParentCompatibleIds)
                        .Concat(device.ParentDeviceIds)
                        .Append(device.DeviceId)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct(StringComparer.OrdinalIgnoreCase);

                    if (!exactIds.Any(id => InfContainsHardwareId(contents, id)))
                        continue;

                    if (!matches.TryGetValue(infPath, out var ids))
                    {
                        ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        matches.Add(infPath, ids);
                    }

                    ids.Add(device.DeviceId);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                // Ignore unreadable/non-text driver packages and continue safely.
            }
        }

        return matches
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new InfCandidate(pair.Key, pair.Value.OrderBy(x => x).ToArray()))
            .ToArray();
    }

    private static bool InfContainsHardwareId(string infContents, string hardwareId)
    {
        // Match a complete INF token, not a shortened vendor/product prefix.
        var searchFrom = 0;
        while (searchFrom < infContents.Length)
        {
            var index = infContents.IndexOf(
                hardwareId, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;

            var end = index + hardwareId.Length;
            var validStart = index == 0 || IsInfTokenBoundary(infContents[index - 1]);
            var validEnd = end == infContents.Length || IsInfTokenBoundary(infContents[end]);
            if (validStart && validEnd)
                return true;

            searchFrom = index + 1;
        }

        return false;
    }

    private static bool IsInfTokenBoundary(char value) =>
        char.IsWhiteSpace(value) || value is ',' or '"' or ';' or ']' or '=';

    private static DriverPackageInstallResult InstallPackage(
        string infPath,
        IReadOnlyList<string> matchedDeviceIds)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("/add-driver");
            startInfo.ArgumentList.Add(infPath);
            startInfo.ArgumentList.Add("/install");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new DriverPackageInstallResult(
                    infPath, matchedDeviceIds, -1, "Failed to start PnPUtil", string.Empty);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(120_000))
            {
                process.Kill(entireProcessTree: true);
                return new DriverPackageInstallResult(
                    infPath, matchedDeviceIds, -1, "Timed out after 120 seconds", string.Empty);
            }

            Task.WaitAll(stdoutTask, stderrTask);
            var output = (stdoutTask.Result + Environment.NewLine + stderrTask.Result).Trim();
            if (output.Length > 3000)
                output = output[..3000];

            return new DriverPackageInstallResult(
                infPath,
                matchedDeviceIds,
                process.ExitCode,
                process.ExitCode == 0
                    ? "PnPUtil accepted the package; Windows decides whether its signature and rank permit installation"
                    : "PnPUtil rejected the package or could not install it",
                output);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return new DriverPackageInstallResult(
                infPath, matchedDeviceIds, -1, $"Installation failed: {ex.Message}", string.Empty);
        }
    }

    public static void RunSelfTests()
    {
        const string inf = "[Models]\r\n%Device%=Install, HDAUDIO\\FUNC_01&VEN_10EC&DEV_0256\r\n";
        if (!InfContainsHardwareId(inf, @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0256") ||
            InfContainsHardwareId(inf, @"HDAUDIO\FUNC_01&VEN_10EC"))
        {
            throw new InvalidOperationException("Exact INF hardware ID matcher self-test failed.");
        }
    }

    private sealed record InfCandidate(string Path, IReadOnlyList<string> DeviceIds);
}
