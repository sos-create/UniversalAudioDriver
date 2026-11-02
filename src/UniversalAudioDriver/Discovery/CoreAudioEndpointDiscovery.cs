using NAudio.CoreAudioApi;
using UniversalAudioDriver.Models;

namespace UniversalAudioDriver.Discovery;

public sealed class CoreAudioEndpointDiscovery
{
    public IReadOnlyList<AudioEndpointInfo> ScanRenderEndpoints()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Core Audio is available only on Windows.");

        using var enumerator = new MMDeviceEnumerator();
        var endpoints = new List<AudioEndpointInfo>();
        var defaultIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ReadDefault(enumerator, Role.Console, "Console", defaultIds);
        ReadDefault(enumerator, Role.Multimedia, "Multimedia", defaultIds);
        ReadDefault(enumerator, Role.Communications, "Communications", defaultIds);

        // MMDeviceCollection in NAudio 2.2.1 is not IDisposable.
        var collection = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All);
        foreach (var device in collection)
        {
            using (device)
            {
                var state = device.State.ToString();
                var isDefault = defaultIds.Values.Contains(device.ID, StringComparer.OrdinalIgnoreCase);

                bool? muted = null;
                float? volume = null;
                try
                {
                    muted = device.AudioEndpointVolume.Mute;
                    volume = device.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;
                }
                catch
                {
                    // Some endpoints do not expose endpoint-volume controls.
                }

                var roles = string.Join(", ", defaultIds
                    .Where(pair => string.Equals(pair.Value, device.ID, StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.Key));
                var formFactor = ReadFormFactor(device);

                endpoints.Add(new AudioEndpointInfo(
                    device.FriendlyName,
                    device.ID,
                    state,
                    string.IsNullOrEmpty(roles) ? "Not default" : roles,
                    isDefault,
                    muted,
                    volume,
                    formFactor));
            }
        }

        return endpoints.OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name).ToArray();
    }

    private static string ReadFormFactor(MMDevice device)
    {
        try
        {
            var property = device.Properties[PropertyKeys.PKEY_AudioEndpoint_FormFactor];
            var value = property?.Value;
            if (value is null)
                return "Unknown";

            var formFactor = Convert.ToUInt32(value);
            return formFactor switch
            {
                0 => "RemoteNetworkDevice",
                1 => "Speakers",
                2 => "LineLevel",
                3 => "Headphones",
                4 => "Microphone",
                5 => "Headset",
                6 => "Handset",
                7 => "UnknownDigitalPassthrough",
                8 => "SPDIF",
                9 => "HDMI",
                10 => "UnknownFormFactor",
                _ => $"Unknown ({formFactor})"
            };
        }
        catch
        {
            // Some OEM endpoints do not publish the optional form-factor property.
            return "Unknown";
        }
    }

    private static void ReadDefault(
        MMDeviceEnumerator enumerator,
        Role role,
        string roleName,
        IDictionary<string, string> defaults)
    {
        try
        {
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
            defaults[roleName] = device.ID;
        }
        catch (Exception)
        {
            // Windows may not have a default endpoint for this role.
        }
    }
}
