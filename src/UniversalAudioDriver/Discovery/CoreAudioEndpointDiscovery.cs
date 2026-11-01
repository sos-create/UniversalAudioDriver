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

        ReadDefault(enumerator, Role.Multimedia, "Multimedia", defaultIds);
        ReadDefault(enumerator, Role.Communications, "Communications", defaultIds);

        using var collection = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All);
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

                endpoints.Add(new AudioEndpointInfo(
                    device.FriendlyName,
                    device.ID,
                    state,
                    string.IsNullOrEmpty(roles) ? "Not default" : roles,
                    isDefault,
                    muted,
                    volume));
            }
        }

        return endpoints.OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name).ToArray();
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
