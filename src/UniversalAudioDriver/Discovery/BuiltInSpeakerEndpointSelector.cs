using UniversalAudioDriver.Models;

namespace UniversalAudioDriver.Discovery;

/// <summary>
/// Conservative endpoint classifier. It prefers the native Windows speaker
/// form factor and falls back to localized endpoint names when that property is absent.
/// </summary>
public static class BuiltInSpeakerEndpointSelector
{
    private static readonly string[] ExternalMarkers =
    {
        "HDMI", "DISPLAYPORT", "BLUETOOTH", "BTHENUM", "USB",
        "HEADPHONE", "HEADSET", "DOCK", "WIRELESS",
        "НАУШНИК", "ГАРНИТУР", "БЕСПРОВОД", "ДОК-СТАНЦИЯ",
        "KOPFHÖRER", "KOPFHORER", "HEADPHONES",
        "ÉCOUTEURS", "ECOUTEURS", "CASQUE"
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

        var endpointSignature = string.Join(" ",
            endpoint.Name, endpoint.ControllerDeviceId ?? string.Empty).ToUpperInvariant();
        if (ExternalMarkers.Any(endpointSignature.Contains))
            return false;

        if (endpoint.FormFactor.Equals("Speakers", StringComparison.OrdinalIgnoreCase))
            return true;

        if (endpoint.FormFactor.Equals("Headphones", StringComparison.OrdinalIgnoreCase) ||
            endpoint.FormFactor.Equals("Headset", StringComparison.OrdinalIgnoreCase) ||
            endpoint.FormFactor.Equals("HDMI", StringComparison.OrdinalIgnoreCase) ||
            endpoint.FormFactor.Equals("SPDIF", StringComparison.OrdinalIgnoreCase) ||
            endpoint.FormFactor.Equals("LineLevel", StringComparison.OrdinalIgnoreCase) ||
            endpoint.FormFactor.Equals("Microphone", StringComparison.OrdinalIgnoreCase) ||
            endpoint.FormFactor.Equals("Handset", StringComparison.OrdinalIgnoreCase) ||
            endpoint.FormFactor.Equals("RemoteNetworkDevice", StringComparison.OrdinalIgnoreCase))
            return false;

        return name.Contains("SPEAKER") ||
               name.Contains("INTERNAL AUDIO") ||
               name.Contains("ДИНАМИК") ||
               name.Contains("LAUTSPRECHER") ||
               name.Contains("ALTAVOCES") ||
               name.Contains("ALTOPARLANTI") ||
               name.Contains("HAUT-PARLEUR");
    }

    public static void RunSelfTests()
    {
        var builtIn = new AudioEndpointInfo(
            "Speakers (Realtek(R) Audio)", "endpoint-speakers", "Active",
            "Multimedia", true, false, 50, "Speakers", @"HDAUDIO\FUNC_01&VEN_10EC");
        var russianBuiltIn = new AudioEndpointInfo(
            "Динамики (Realtek(R) Audio)", "endpoint-russian-speakers", "Active",
            "Not default", false, false, 50, "Speakers", null);
        var germanBuiltIn = new AudioEndpointInfo(
            "Lautsprecher (Realtek(R) Audio)", "endpoint-german-speakers", "Active",
            "Not default", false, false, 50, "Speakers", null);
        var genericBuiltIn = new AudioEndpointInfo(
            "Realtek Audio", "endpoint-generic", "Active",
            "Not default", false, false, 50, "Speakers", null);
        var frenchBuiltIn = new AudioEndpointInfo(
            "Haut-parleurs (Realtek Audio)", "endpoint-french-speakers", "Active",
            "Not default", false, false, 50, "Speakers", null);
        var hdmi = new AudioEndpointInfo(
            "Speakers (NVIDIA High Definition Audio HDMI)", "endpoint-hdmi",
            "Active", "Not default", false, false, 50, "HDMI", null);
        var headphones = new AudioEndpointInfo(
            "Headphones (Realtek Audio)", "endpoint-headphones",
            "Active", "Not default", false, false, 50, "Headphones", null);
        var usbSpeaker = new AudioEndpointInfo(
            "Speakers (USB PnP Sound Device)", "endpoint-usb-speakers",
            "Active", "Not default", false, false, 50, "Speakers", @"USB\\VID_1234&PID_5678");
        var disabled = new AudioEndpointInfo(
            "Speakers (Cirrus Audio)", "endpoint-disabled",
            "Disabled", "Not default", false, false, 50, "Speakers", null);

        if (!IsLikelyBuiltInSpeaker(builtIn) ||
            !IsLikelyBuiltInSpeaker(russianBuiltIn) ||
            !IsLikelyBuiltInSpeaker(germanBuiltIn) ||
            !IsLikelyBuiltInSpeaker(genericBuiltIn) ||
            !IsLikelyBuiltInSpeaker(frenchBuiltIn) ||
            IsLikelyBuiltInSpeaker(hdmi) ||
            IsLikelyBuiltInSpeaker(headphones) ||
            IsLikelyBuiltInSpeaker(usbSpeaker) ||
            IsLikelyBuiltInSpeaker(disabled) ||
            Select(new[] { hdmi, builtIn })?.Id != builtIn.Id ||
            Select(new[] { hdmi, builtIn })?.ControllerDeviceId != @"HDAUDIO\FUNC_01&VEN_10EC")
        {
            throw new InvalidOperationException("Built-in speaker endpoint classifier self-test failed.");
        }
    }
}
