using System.Runtime.InteropServices;
using UniversalAudioDriver.Models;

namespace UniversalAudioDriver.Engine;

/// <summary>
/// Best-effort default render endpoint configuration through Windows'
/// undocumented IPolicyConfig COM interface. Core Audio's public MMDevice API
/// supports reading defaults but does not expose a public setter.
/// The call is isolated and failures are reported; it never installs drivers.
/// </summary>
public sealed class WindowsDefaultAudioEndpointManager
{
    public DefaultAudioEndpointConfigurationResult SetBuiltInSpeakersAsDefault(
        AudioEndpointInfo endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!OperatingSystem.IsWindows())
        {
            return Failed(endpoint, "Default endpoint configuration is available only on Windows.");
        }

        if (!string.Equals(endpoint.State, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return Failed(endpoint, "The selected speaker endpoint is not active.");
        }

        object? policyObject = null;
        var results = new List<AudioEndpointRoleChange>();

        try
        {
            policyObject = new PolicyConfigClient();
            var policy = (IPolicyConfig)policyObject;

            var console = SetRole(policy, endpoint.Id, AudioEndpointRole.Console);
            results.Add(console);

            var multimedia = SetRole(policy, endpoint.Id, AudioEndpointRole.Multimedia);
            results.Add(multimedia);

            var communications = SetRole(policy, endpoint.Id, AudioEndpointRole.Communications);
            results.Add(communications);

            var status = console.Succeeded && multimedia.Succeeded
                ? communications.Succeeded
                    ? "Built-in speakers set as the default for Console, Multimedia and Communications."
                    : "Built-in speakers set as the system playback default. Communications role could not be changed."
                : "Windows rejected one or more default endpoint changes. Check each role's HRESULT.";

            return new DefaultAudioEndpointConfigurationResult(
                true,
                endpoint.Name,
                endpoint.Id,
                console.Succeeded,
                multimedia.Succeeded,
                communications.Succeeded,
                status,
                results);
        }
        catch (Exception ex)
        {
            return new DefaultAudioEndpointConfigurationResult(
                true,
                endpoint.Name,
                endpoint.Id,
                false,
                false,
                false,
                $"Windows default endpoint configuration failed: {ex.Message}",
                results);
        }
        finally
        {
            if (policyObject is not null && Marshal.IsComObject(policyObject))
            {
                try { Marshal.FinalReleaseComObject(policyObject); }
                catch (InvalidComObjectException) { }
            }
        }
    }

    public static DefaultAudioEndpointConfigurationResult VerifyReadback(
        DefaultAudioEndpointConfigurationResult result,
        IEnumerable<AudioEndpointInfo> endpoints)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(endpoints);

        if (!result.Attempted)
            return result;

        var selected = endpoints.FirstOrDefault(endpoint =>
            string.Equals(endpoint.Id, result.EndpointId, StringComparison.OrdinalIgnoreCase));

        var console = HasRole(selected, "Console");
        var multimedia = HasRole(selected, "Multimedia");
        var communications = HasRole(selected, "Communications");

        var roles = new[] { "Console", "Multimedia", "Communications" };
        var updatedResults = roles.Select(role =>
        {
            var verified = role switch
            {
                "Console" => console,
                "Multimedia" => multimedia,
                _ => communications
            };

            var previous = result.RoleResults.FirstOrDefault(item =>
                string.Equals(item.Role, role, StringComparison.OrdinalIgnoreCase));
            var hresult = previous?.HResult ?? unchecked((int)0x80004005);
            var status = verified
                ? "Verified: Windows reports this speaker endpoint as the default for this role."
                : selected is null
                    ? "Not verified: the selected speaker endpoint was not returned by Core Audio after the change."
                    : "Not verified: Core Audio does not report this speaker endpoint as the default for this role.";

            return new AudioEndpointRoleChange(role, hresult, verified, status);
        }).ToArray();

        var summary = console && multimedia
            ? communications
                ? "Core Audio readback confirms built-in speakers are default for Console, Multimedia and Communications."
                : "Core Audio readback confirms built-in speakers are default for Console and Multimedia; Communications remains unchanged."
            : "Core Audio readback did not confirm built-in speakers as both Console and Multimedia default output.";

        return result with
        {
            ConsoleChanged = console,
            MultimediaChanged = multimedia,
            CommunicationsChanged = communications,
            Status = summary,
            RoleResults = updatedResults
        };
    }

    private static bool HasRole(AudioEndpointInfo? endpoint, string role)
    {
        if (endpoint is null)
            return false;

        return endpoint.Role
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    public static void RunSelfTests()
    {
        var readbackBase = new DefaultAudioEndpointConfigurationResult(
            true, "Speakers", "readback-id",
            false, false, false, "not yet confirmed",
            Array.Empty<AudioEndpointRoleChange>());
        var readbackEndpoints = new[]
        {
            new AudioEndpointInfo(
                "Speakers (Realtek Audio)", "readback-id", "Active",
                "Console, Multimedia, Communications", true, false, 50, "Speakers", null)
        };
        var verifiedReadback = VerifyReadback(readbackBase, readbackEndpoints);
        var missingMultimedia = VerifyReadback(
            readbackBase,
            new[]
            {
                new AudioEndpointInfo(
                    "Speakers (Realtek Audio)", "readback-id", "Active",
                    "Console", true, false, 50, "Speakers", null)
            });

        var fullSuccess = new DefaultAudioEndpointConfigurationResult(
            true, "Speakers", "test-id",
            true, true, true, "ok", Array.Empty<AudioEndpointRoleChange>());
        var communicationsFailure = new DefaultAudioEndpointConfigurationResult(
            true, "Speakers", "test-id",
            true, true, false, "partial", Array.Empty<AudioEndpointRoleChange>());
        var multimediaFailure = new DefaultAudioEndpointConfigurationResult(
            true, "Speakers", "test-id",
            true, false, true, "failed", Array.Empty<AudioEndpointRoleChange>());

        var checks = new Dictionary<string, bool>
        {
            ["Console+Multimedia success"] = fullSuccess.SystemPlaybackDefaultChanged,
            ["Communications failure must not block playback"] = communicationsFailure.SystemPlaybackDefaultChanged,
            ["Multimedia failure must block playback"] = !multimediaFailure.SystemPlaybackDefaultChanged,
            ["Readback confirms speaker default"] = verifiedReadback.SystemPlaybackDefaultChanged,
            ["Readback confirms all three roles"] = verifiedReadback.RoleResults.All(role => role.Succeeded),
            ["Readback rejects missing Multimedia role"] = !missingMultimedia.SystemPlaybackDefaultChanged,
            ["Console role enum is 0"] = (int)AudioEndpointRole.Console == 0,
            ["Multimedia role enum is 1"] = (int)AudioEndpointRole.Multimedia == 1,
            ["Communications role enum is 2"] = (int)AudioEndpointRole.Communications == 2
        };

        var failedChecks = checks
            .Where(check => !check.Value)
            .Select(check => check.Key)
            .ToArray();

        if (failedChecks.Length > 0)
        {
            throw new InvalidOperationException(
                "Default speaker routing self-test failed: " +
                string.Join("; ", failedChecks));
        }
    }

    private static AudioEndpointRoleChange SetRole(
        IPolicyConfig policy,
        string endpointId,
        AudioEndpointRole role)
    {
        var hresult = policy.SetDefaultEndpoint(endpointId, role);
        var succeeded = hresult >= 0;
        return new AudioEndpointRoleChange(
            role.ToString(),
            hresult,
            succeeded,
            succeeded ? "Default endpoint changed." : $"Windows returned HRESULT 0x{hresult:X8}.");
    }

    private static DefaultAudioEndpointConfigurationResult Failed(
        AudioEndpointInfo endpoint,
        string status) =>
        new(
            false,
            endpoint.Name,
            endpoint.Id,
            false,
            false,
            false,
            status,
            Array.Empty<AudioEndpointRoleChange>());

    private enum AudioEndpointRole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2
    }

    // IPolicyConfig is an undocumented Windows COM interface. Keep the complete
    // vtable declaration in the exact native order so SetDefaultEndpoint maps
    // to the correct slot. Unused pointer parameters are represented by IntPtr.
    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig]
        int GetMixFormat(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            out IntPtr format);

        [PreserveSig]
        int GetDeviceFormat(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            int defaultFormat,
            out IntPtr format);

        [PreserveSig]
        int ResetDeviceFormat(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId);

        [PreserveSig]
        int SetDeviceFormat(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            IntPtr endpointFormat,
            IntPtr mixFormat);

        [PreserveSig]
        int GetProcessingPeriod(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            int defaultPeriod,
            out long defaultPeriodValue,
            out long minimumPeriodValue);

        [PreserveSig]
        int SetProcessingPeriod(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            IntPtr period);

        [PreserveSig]
        int GetShareMode(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            IntPtr mode);

        [PreserveSig]
        int SetShareMode(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            IntPtr mode);

        [PreserveSig]
        int GetPropertyValue(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            IntPtr key,
            IntPtr value);

        [PreserveSig]
        int SetPropertyValue(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            IntPtr key,
            IntPtr value);

        [PreserveSig]
        int SetDefaultEndpoint(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            AudioEndpointRole role);

        [PreserveSig]
        int SetEndpointVisibility(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            int visible);
    }

    [ComImport]
    [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient
    {
    }
}
