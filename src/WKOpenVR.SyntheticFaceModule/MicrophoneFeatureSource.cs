using NAudio.Wave;

namespace WKOpenVR.SyntheticFaceModule;

public sealed class MicrophoneFeatureSource : IAudioFeatureSource
{
    private readonly object gate = new();
    private readonly WaveInEvent capture;
    private AudioFeatureFrame latest;
    private bool hasFrame;
    private bool started;

    public MicrophoneFeatureSource(int deviceNumber = 0, int sampleRate = 16000)
    {
        capture = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            BufferMilliseconds = 20,
            WaveFormat = new WaveFormat(sampleRate, 16, 1)
        };
        capture.DataAvailable += OnDataAvailable;
    }

    public void Start()
    {
        if (started)
        {
            return;
        }

        capture.StartRecording();
        started = true;
    }

    public bool TryRead(out AudioFeatureFrame frame)
    {
        lock (gate)
        {
            frame = latest;
            return hasFrame;
        }
    }

    public void Dispose()
    {
        if (started)
        {
            capture.StopRecording();
            started = false;
        }

        capture.DataAvailable -= OnDataAvailable;
        capture.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        int samples = args.BytesRecorded / 2;
        if (samples <= 0)
        {
            return;
        }

        double sumSquares = 0;
        int crossings = 0;
        short previous = 0;
        bool havePrevious = false;

        for (int i = 0; i < args.BytesRecorded; i += 2)
        {
            short sample = BitConverter.ToInt16(args.Buffer, i);
            float normalized = sample / 32768.0f;
            sumSquares += normalized * normalized;

            if (havePrevious && ((sample < 0 && previous >= 0) || (sample >= 0 && previous < 0)))
            {
                crossings++;
            }

            previous = sample;
            havePrevious = true;
        }

        var next = new AudioFeatureFrame(
            Rms: (float)Math.Sqrt(sumSquares / samples),
            ZeroCrossingRate: crossings / (float)Math.Max(1, samples - 1),
            SampleRate: capture.WaveFormat.SampleRate);

        lock (gate)
        {
            latest = next;
            hasFrame = true;
        }
    }
}
