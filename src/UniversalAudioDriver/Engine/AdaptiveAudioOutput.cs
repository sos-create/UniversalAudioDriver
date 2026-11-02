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
    string SelectionReason);

/// <summary>
/// Selects an active Windows render endpoint and uses its reported shared-mode
/// mix format. The physical controller/codec remain managed by the installed
/// Windows PnP driver stack; this class does not replace a kernel miniport.
/// </summary>
public sealed class AdaptiveAudioOutput
{
    public AdaptiveOutputResult PlayTestTone(TimeSpan? duration = null)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Adaptive WASAPI output is available only on Windows.");

        using var enumerator = new MMDeviceEnumerator();
        var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        if (endpoints.Count == 0)
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

        selected ??= endpoints.FirstOrDefault();
        if (selected is null)
            throw new InvalidOperationException("No usable active audio endpoint was found.");

        using (selected)
        {
            var mixFormat = selected.AudioClient.MixFormat;
            var sampleRate = mixFormat.SampleRate;
            var channels = Math.Max(1, mixFormat.Channels);
            if (sampleRate < 8000 || sampleRate > 384000)
                throw new InvalidOperationException($"Windows reported an unsupported mix rate: {sampleRate} Hz.");

            var signal = new SignalGenerator(sampleRate, channels)
            {
                Type = SignalGeneratorType.Sin,
                Frequency = 440,
                Gain = 0.08
            };

            using var output = new WasapiOut(selected, AudioClientShareMode.Shared, true, 120);
            output.Init(new SampleToWaveProvider(signal));
            output.Play();

            try
            {
                Thread.Sleep(duration ?? TimeSpan.FromSeconds(1.5));
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
                reason);
        }
    }
}
