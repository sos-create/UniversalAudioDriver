using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace UniversalAudioDriver.Engine;

public sealed class AudioPlaybackTest
{
    public void PlayDefaultEndpointTone(TimeSpan? duration = null)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WASAPI playback is available only on Windows.");

        var toneDuration = duration ?? TimeSpan.FromSeconds(1.5);
        var signal = new SignalGenerator(44100, 2)
        {
            Type = SignalGeneratorType.Sin,
            Frequency = 440,
            Gain = 0.12
        };

        using var output = new WasapiOut(AudioClientShareMode.Shared, 150);
        output.Init(new SampleToWaveProvider(signal));
        output.Play();

        try
        {
            Thread.Sleep(toneDuration);
        }
        finally
        {
            output.Stop();
        }
    }
}
