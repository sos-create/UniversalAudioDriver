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
