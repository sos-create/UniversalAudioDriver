using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace UniversalAudioDriver.Engine;

public sealed record AdaptiveOutputResult(
    string EndpointName,
    string EndpointId,
    int SampleRate,
    int Channels,
    string ShareMode,
    string SelectionReason,
    string OutputPreparation);

/// <summary>
/// Uses Windows Core Audio/WASAPI shared mode. The physical controller/codec remain
/// managed by the installed Windows PnP driver stack.
/// </summary>
public sealed class AdaptiveAudioOutput
{
    public AdaptiveOutputResult PlayTestTone(TimeSpan? duration = null)
    {
        EnsureWindows();

        using var enumerator = new MMDeviceEnumerator();
        var candidates = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Cast<MMDevice>()
            .ToList();

        if (candidates.Count == 0)
            throw new InvalidOperationException("Windows has no active audio render endpoint.");

        MMDevice? selected = null;
        var reason = "First active Windows render endpoint (default endpoint unavailable).";

        try
        {
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (defaultDevice.State == DeviceState.Active)
            {
                selected = defaultDevice;
                reason = "Windows default multimedia playback endpoint.";
            }
            else
            {
                defaultDevice.Dispose();
            }
        }
        catch
        {
            // A missing default endpoint is handled by selecting an active endpoint below.
        }

        if (selected is null)
        {
            selected = candidates[0];
            reason = "First active Windows render endpoint; no active default multimedia endpoint.";
            candidates.RemoveAt(0);
        }

        foreach (var candidate in candidates)
            candidate.Dispose();

        using (selected)
            return Play(selected, reason, duration);
    }

    public AdaptiveOutputResult PlayTestToneOnEndpoint(
        string endpointId,
        TimeSpan? duration = null)
    {
        EnsureWindows();
        if (string.IsNullOrWhiteSpace(endpointId))
            throw new ArgumentException("An endpoint ID is required.", nameof(endpointId));

        using var enumerator = new MMDeviceEnumerator();
        var candidates = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Cast<MMDevice>()
            .ToList();

        MMDevice? selected = null;
        foreach (var candidate in candidates)
        {
            if (string.Equals(candidate.ID, endpointId, StringComparison.OrdinalIgnoreCase))
            {
                selected = candidate;
                continue;
            }

            candidate.Dispose();
        }

        if (selected is null)
            throw new InvalidOperationException("The selected built-in speaker endpoint is no longer active.");

        using (selected)
            return Play(selected, "Explicitly selected active built-in speaker endpoint.", duration);
    }

    private static AdaptiveOutputResult Play(
        MMDevice selected,
        string selectionReason,
        TimeSpan? duration)
    {
        // Windows can route a stream successfully while the endpoint is muted or
        // its endpoint volume is at zero. Prepare only the selected built-in endpoint:
        // unmute it and apply a conservative 20% floor, leaving higher user levels alone.
        var outputPreparation = PrepareEndpointVolume(selected);
        var mixFormat = selected.AudioClient.MixFormat;
        var sampleRate = mixFormat.SampleRate;
        var channels = Math.Max(1, mixFormat.Channels);
        if (sampleRate < 8000 || sampleRate > 384000)
            throw new InvalidOperationException($"Windows reported an unsupported mix rate: {sampleRate} Hz.");

        var signal = new SignalGenerator(sampleRate, channels)
        {
            Type = SignalGeneratorType.Sin,
            Frequency = 440,
            Gain = 0.05
        };

        using var output = new WasapiOut(selected, AudioClientShareMode.Shared, true, 120);
        output.Init(new SampleToWaveProvider(signal));
        output.Play();

        try
        {
            Thread.Sleep(duration ?? TimeSpan.FromSeconds(1.2));
        }
        finally
        {
            output.Stop();
        }

        return new AdaptiveOutputResult(
            selected.FriendlyName,
            selected.ID,
            sampleRate,
            channels,
            "WASAPI shared mode",
            selectionReason,
            outputPreparation);
    }

    private static string PrepareEndpointVolume(MMDevice selected)
    {
        try
        {
            var endpointVolume = selected.AudioEndpointVolume;
            var actions = new List<string>();

            if (endpointVolume.Mute)
            {
                endpointVolume.Mute = false;
                actions.Add("endpoint mute disabled");
            }

            var currentVolume = endpointVolume.MasterVolumeLevelScalar;
            if (currentVolume < 0.20f)
            {
                endpointVolume.MasterVolumeLevelScalar = 0.20f;
                actions.Add($"endpoint volume raised from {currentVolume:P0} to 20%");
                currentVolume = endpointVolume.MasterVolumeLevelScalar;
            }

            return actions.Count == 0
                ? $"Endpoint was already unmuted at {currentVolume:P0}."
                : string.Join("; ", actions) + $"; current endpoint volume {currentVolume:P0}.";
        }
        catch (Exception ex)
        {
            // Playback is still attempted; the report makes clear that the
            // endpoint's mute/volume state could not be prepared or verified.
            return $"Could not change endpoint mute/volume: {ex.Message}";
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Adaptive WASAPI output is available only on Windows.");
    }
}
