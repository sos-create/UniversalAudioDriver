using System.Runtime.InteropServices;
using UniversalAudioDriver.Models;

namespace UniversalAudioDriver.Engine;

/// <summary>
/// Uses the Windows Update Agent to find driver updates whose declared driver
/// hardware ID is an exact hardware/compatible ID of an audio-related device
/// that currently reports a PnP problem. Windows Update performs package
/// applicability, signature validation and installation.
/// </summary>
public sealed class WindowsUpdateDriverInstaller
{
    private static readonly HashSet<uint> ProblemCodes = new() { 10, 28, 31, 43 };

    public WindowsUpdateDriverInstallResult InstallMatchingUpdates(
        IReadOnlyList<AudioDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Update Agent is available only on Windows.");

        var targets = devices
            .Where(device => ProblemCodes.Contains(device.ProblemCode))
            .Where(device => !string.Equals(device.PnpClass, "AudioEndpoint", StringComparison.OrdinalIgnoreCase))
            .Select(device => new
            {
                Device = device,
                Ids = device.HardwareIds
                    .Concat(device.CompatibleIds)
                    .Concat(device.ParentHardwareIds)
                    .Concat(device.ParentCompatibleIds)
                    .Append(device.DeviceId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
            })
            .ToArray();

        if (targets.Length == 0)
            return Empty("No audio-related PnP devices have a recoverable problem code.");

        object? sessionObject = null;
        object? searcherObject = null;
        object? searchResultObject = null;
        object? updatesObject = null;
        object? collectionObject = null;
        object? downloaderObject = null;
        object? downloadResultObject = null;
        object? installerObject = null;
        object? installResultObject = null;

        try
        {
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session", throwOnError: true)
                ?? throw new InvalidOperationException("Windows Update Agent session is not registered.");
            sessionObject = Activator.CreateInstance(sessionType)
                ?? throw new InvalidOperationException("Could not create Windows Update Agent session.");

            dynamic session = sessionObject;
            searcherObject = session.CreateUpdateSearcher();
            dynamic searcher = searcherObject;
            searchResultObject = searcher.Search("IsInstalled=0 and Type='Driver'");
            dynamic searchResult = searchResultObject;
            updatesObject = searchResult.Updates;
            dynamic updates = updatesObject;

            var availableCount = Convert.ToInt32(updates.Count);
            var matched = new List<MatchedUpdate>();

            for (var index = 0; index < availableCount; index++)
            {
                object updateObject = updates.Item(index);
                dynamic update = updateObject;

                string hardwareId;
                string title;
                string provider;
                bool eulaRequired;
                bool eulaAccepted;

                try
                {
                    hardwareId = (Convert.ToString(update.DriverHardwareID) ?? string.Empty).Trim();
                    title = Convert.ToString(update.Title) ?? "Unnamed driver update";
                    provider = Convert.ToString(update.DriverProvider) ?? "Unknown provider";
                    var eulaText = Convert.ToString(update.EulaText) ?? string.Empty;
                    eulaRequired = !string.IsNullOrWhiteSpace(eulaText);
                    eulaAccepted = IsEulaAccepted(eulaText, Convert.ToBoolean(update.EulaAccepted));
                }
                catch
                {
                    Release(updateObject);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(hardwareId))
                {
                    Release(updateObject);
                    continue;
                }

                var matchedDevices = targets
                    .Where(target => MatchesExactHardwareId(target.Ids, hardwareId))
                    .Select(target => target.Device.DeviceId)
                    .ToArray();

                if (matchedDevices.Length == 0)
                {
                    Release(updateObject);
                    continue;
                }

                matched.Add(new MatchedUpdate(
                    updateObject,
                    title,
                    hardwareId,
                    provider,
                    eulaRequired,
                    eulaAccepted));
            }

            var accepted = matched.Where(update => update.EulaAccepted).ToArray();
            if (accepted.Length == 0)
            {
                var status = matched.Count == 0
                    ? "Windows Update returned no driver package with an exact audio device or parent-controller hardware/compatible ID match."
                    : "Matching updates require an EULA that has not been accepted. No update was installed automatically.";
                var skipped = matched.Select(update => new WindowsUpdateDriverPackageResult(
                    update.Title, update.HardwareId, update.Provider, update.EulaRequired,
                    -1, -1, false,
                    update.EulaAccepted ? "Not installed" : "Skipped: EULA acceptance is required")).ToArray();

                foreach (var update in matched)
                    Release(update.UpdateObject);

                return new WindowsUpdateDriverInstallResult(
                    availableCount, matched.Count, 0, false, false, false, status, skipped);
            }

            var collectionType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl", throwOnError: true)
                ?? throw new InvalidOperationException("Windows Update collection type is not registered.");
            collectionObject = Activator.CreateInstance(collectionType)
                ?? throw new InvalidOperationException("Could not create Windows Update collection.");
            dynamic collection = collectionObject;

            foreach (var update in accepted)
                collection.Add(update.UpdateObject);

            downloaderObject = session.CreateUpdateDownloader();
            dynamic downloader = downloaderObject;
            downloader.Updates = collection;
            downloadResultObject = downloader.Download();
            dynamic downloadResult = downloadResultObject;
            var downloadCode = Convert.ToInt32(downloadResult.ResultCode);
            var downloadSucceeded = downloadCode is 2 or 3;

            if (!downloadSucceeded)
            {
                var failedPackages = accepted.Select(update => new WindowsUpdateDriverPackageResult(
                    update.Title, update.HardwareId, update.Provider, false,
                    downloadCode, -1, false,
                    $"Windows Update download result code: {downloadCode}")).ToArray();

                foreach (var update in matched)
                    Release(update.UpdateObject);

                return new WindowsUpdateDriverInstallResult(
                    availableCount, matched.Count, accepted.Length,
                    false, false, false,
                    $"Windows Update could not download the matching driver packages (result {downloadCode}).",
                    failedPackages);
            }

            installerObject = session.CreateUpdateInstaller();
            dynamic installer = installerObject;
            installer.Updates = collection;
            installResultObject = installer.Install();
            dynamic installResult = installResultObject;

            var aggregateInstallCode = Convert.ToInt32(installResult.ResultCode);
            var rebootRequired = Convert.ToBoolean(installResult.RebootRequired);
            var packageResults = new List<WindowsUpdateDriverPackageResult>();

            for (var index = 0; index < accepted.Length; index++)
            {
                var update = accepted[index];
                var itemCode = aggregateInstallCode;
                var itemReboot = rebootRequired;
                var itemStatus = $"Windows Update aggregate result: {aggregateInstallCode}";

                try
                {
                    dynamic itemResult = installResult.GetUpdateResult(index);
                    itemCode = Convert.ToInt32(itemResult.ResultCode);
                    itemReboot = Convert.ToBoolean(itemResult.RebootRequired);
                    itemStatus = itemCode switch
                    {
                        2 => "Installed successfully",
                        3 => "Installed with warnings",
                        _ => $"Installation result code: {itemCode}"
                    };
                }
                catch
                {
                    // Keep the aggregate result if a per-update result is unavailable.
                }

                packageResults.Add(new WindowsUpdateDriverPackageResult(
                    update.Title,
                    update.HardwareId,
                    update.Provider,
                    false,
                    downloadCode,
                    itemCode,
                    itemReboot,
                    itemStatus));
            }

            var installSucceeded = aggregateInstallCode is 2 or 3;
            foreach (var update in matched)
                Release(update.UpdateObject);

            return new WindowsUpdateDriverInstallResult(
                availableCount,
                matched.Count,
                accepted.Length,
                downloadSucceeded,
                installSucceeded,
                rebootRequired,
                installSucceeded
                    ? "Windows Update completed installation of updates matching exact audio device or parent-controller IDs."
                    : $"Windows Update returned installation result code {aggregateInstallCode}.",
                packageResults);
        }
        catch (Exception ex)
        {
            return Empty($"Windows Update driver search/install was unavailable: {ex.Message}");
        }
        finally
        {
            Release(installResultObject);
            Release(installerObject);
            Release(downloadResultObject);
            Release(downloaderObject);
            Release(collectionObject);
            Release(updatesObject);
            Release(searchResultObject);
            Release(searcherObject);
            Release(sessionObject);
        }
    }

    private static bool IsEulaAccepted(string? eulaText, bool accepted) =>
        string.IsNullOrWhiteSpace(eulaText) || accepted;

    private static bool MatchesExactHardwareId(
        IReadOnlySet<string> deviceIds,
        string updateHardwareId) =>
        !string.IsNullOrWhiteSpace(updateHardwareId) &&
        deviceIds.Contains(updateHardwareId.Trim());

    public static void RunSelfTests()
    {
        var ids = new HashSet<string>(
            new[]
            {
                @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0256",
                @"HDAUDIO\FUNC_01&VEN_10EC",
                @"PCI\VEN_8086&DEV_9D70"
            },
            StringComparer.OrdinalIgnoreCase);

        if (!MatchesExactHardwareId(ids, @"hdaudio\func_01&ven_10ec&dev_0256") ||
            !MatchesExactHardwareId(ids, @"HDAUDIO\FUNC_01&VEN_10EC") ||
            !MatchesExactHardwareId(ids, @"PCI\VEN_8086&DEV_9D70") ||
            MatchesExactHardwareId(ids, @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0256&SUBSYS_12345678") ||
            !IsEulaAccepted(null, false) ||
            !IsEulaAccepted(string.Empty, false) ||
            !IsEulaAccepted("License terms", true) ||
            IsEulaAccepted("License terms", false))
        {
            throw new InvalidOperationException("Windows Update exact hardware ID or EULA selection self-test failed.");
        }
    }

    private static WindowsUpdateDriverInstallResult Empty(string status) =>
        new(0, 0, 0, false, false, false, status,
            Array.Empty<WindowsUpdateDriverPackageResult>());

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try { Marshal.FinalReleaseComObject(value); }
            catch (InvalidComObjectException) { }
        }
    }

    private sealed record MatchedUpdate(
        object UpdateObject,
        string Title,
        string HardwareId,
        string Provider,
        bool EulaRequired,
        bool EulaAccepted);
}
