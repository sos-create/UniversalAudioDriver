using UniversalAudioDriver.Models;

namespace UniversalAudioDriver.Discovery;

/// <summary>
/// Conservative endpoint classifier. It deliberately requires a speaker/internal
/// speaker name and rejects clearly external transports.
/// </summary>
public static class BuiltInSpeakerEndpointSelector
{
    private static readonly string[] ExternalMarkers =
    {
        "HDMI", "DISPLAYPORT", "BLUETOOTH", "BTHENUM", "USB AUDIO",
        "HEADPHONE", "HEADSET", "DOCK", "WIRELESS",
        "НАУШНИК", "ГАРНИТУР", "БЕСПРОВОД", "ДОК-СТАНЦИЯ"
    };

    public static AudioEndpointInfo? Select(IEnumerable<AudioEndpointInfo> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints
            .Where(IsLikelyBuiltInSpeaker)
            .OrderByDescending(endpoint => endpoint.IsDefault)
            .ThenBy(endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static bool IsLikelyBuiltInSpeaker(AudioEndpointInfo endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!string.Equals(endpoint.State, "Active", StringComparison.OrdinalIgnoreCase))
            return false;

        var name = endpoint.Name.ToUpperInvariant();
        if (ExternalMarkers.Any(name.Contains))
            return false;

        return name.Contains("SPEAKER") ||
               name.Contains("INTERNAL AUDIO") ||
               name.Contains("ДИНАМИК");
    }

    public static void RunSelfTests()
    {
        var builtIn = new AudioEndpointInfo(
            "Speakers (Realtek(R) Audio)", "endpoint-speakers", "Active",
            "Multimedia", true, false, 50);
        var russianBuiltIn = new AudioEndpointInfo(
            "Динамики (Realtek(R) Audio)", "endpoint-russian-speakers", "Active",
            "Not default", false, false, 50);
        var hdmi = new AudioEndpointInfo(
            "Speakers (NVIDIA High Definition Audio HDMI)", "endpoint-hdmi",
            "Active", "Not default", false, false, 50);
        var headphones = new AudioEndpointInfo(
            "Headphones (Realtek Audio)", "endpoint-headphones",
            "Active", "Not default", false, false, 50);
        var disabled = new AudioEndpointInfo(
            "Speakers (Cirrus Audio)", "endpoint-disabled",
            "Disabled", "Not default", false, false, 50);

        if (!IsLikelyBuiltInSpeaker(builtIn) ||
            !IsLikelyBuiltInSpeaker(russianBuiltIn) ||
            IsLikelyBuiltInSpeaker(hdmi) ||
            IsLikelyBuiltInSpeaker(headphones) ||
            IsLikelyBuiltInSpeaker(disabled) ||
            Select(new[] { hdmi, builtIn })?.Id != builtIn.Id)
        {
            throw new InvalidOperationException("Built-in speaker endpoint classifier self-test failed.");
        }
    }
}
