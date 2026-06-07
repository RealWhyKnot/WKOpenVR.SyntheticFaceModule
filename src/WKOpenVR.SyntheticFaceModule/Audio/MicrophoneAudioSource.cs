using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using NAudio.Wave;
using WKOpenVR.FaceTracking.Sdk;

namespace WKOpenVR.SyntheticFaceModule.Audio;

/// <summary>
/// Captures the microphone as mono 16-bit PCM and turns each buffer into an
/// <see cref="AudioAnalysisFrame"/> via <see cref="AudioAnalyzer"/>. Capture runs on NAudio's
/// callback thread; the latest analysis snapshot is published under a lock and read lock-free-ish by
/// the update loop. A rolling window of the most recent samples feeds the FFT so spectral features
/// reflect a full analysis window even though buffers arrive in smaller chunks.
/// </summary>
public sealed class MicrophoneAudioSource : IAudioAnalysisSource
{
    private readonly object _gate = new();
    private readonly WaveInEvent _capture;
    private readonly AudioAnalyzer _analyzer;
    private readonly int _windowSize;
    private readonly float[] _ring;
    private readonly float[] _window;
    private readonly Stopwatch _clock = new();
    private readonly IFaceModuleLogger? _log;

    private int _ringWrite;
    private int _ringFilled;
    private AudioAnalysisFrame? _latest;
    private bool _started;

    public MicrophoneAudioSource(
        int deviceNumber = -1,
        int sampleRate = 16000,
        int fftSize = 512,
        int melCount = 26,
        int mfccCount = 13,
        IFaceModuleLogger? log = null)
    {
        _analyzer = new AudioAnalyzer(sampleRate, fftSize, melCount, mfccCount);
        _windowSize = fftSize;
        _ring = new float[fftSize];
        _window = new float[fftSize];
        _log = log;

        _capture = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            BufferMilliseconds = 20,
            NumberOfBuffers = 3,
            WaveFormat = new WaveFormat(sampleRate, 16, 1),
        };
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
    }

    /// <summary>
    /// Resolves a capture device index, preferring a friendly-name match when provided, otherwise the
    /// requested index. Returns -1 (default device) when nothing matches.
    /// </summary>
    public static int ResolveDeviceNumber(int preferredNumber, string? preferredName)
    {
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                try
                {
                    WaveInCapabilities caps = WaveInEvent.GetCapabilities(i);
                    if (caps.ProductName.Contains(preferredName, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
                catch (Exception)
                {
                    // Ignore an unreadable device and keep scanning.
                }
            }
        }

        if (preferredNumber >= 0 && preferredNumber < WaveInEvent.DeviceCount)
        {
            return preferredNumber;
        }

        return -1;
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _clock.Restart();
        _capture.StartRecording();
        _started = true;
        _log?.Info($"[synthetic/mic] capture started (device={_capture.DeviceNumber}, {_capture.WaveFormat})");
    }

    public bool TryRead([NotNullWhen(true)] out AudioAnalysisFrame? frame)
    {
        lock (_gate)
        {
            frame = _latest;
            return frame is not null;
        }
    }

    public void Dispose()
    {
        if (_started)
        {
            try
            {
                _capture.StopRecording();
            }
            catch (Exception)
            {
                // Best-effort stop.
            }

            _started = false;
        }

        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _capture.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        int sampleCount = args.BytesRecorded / 2;
        if (sampleCount <= 0)
        {
            return;
        }

        for (int i = 0; i < args.BytesRecorded; i += 2)
        {
            short sample = BitConverter.ToInt16(args.Buffer, i);
            _ring[_ringWrite] = sample / 32768f;
            _ringWrite = (_ringWrite + 1) % _windowSize;
            if (_ringFilled < _windowSize)
            {
                _ringFilled++;
            }
        }

        if (_ringFilled < _windowSize)
        {
            return;
        }

        // Copy the ring into a contiguous, time-ordered window (oldest first).
        for (int i = 0; i < _windowSize; i++)
        {
            _window[i] = _ring[(_ringWrite + i) % _windowSize];
        }

        double timestamp = _clock.Elapsed.TotalSeconds;
        float duration = sampleCount / (float)_capture.WaveFormat.SampleRate;

        var next = new AudioAnalysisFrame(_analyzer.MfccCount);
        _analyzer.Analyze(_window, timestamp, duration, next);

        lock (_gate)
        {
            _latest = next;
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is not null)
        {
            _log?.Warn($"[synthetic/mic] recording stopped with error: {args.Exception.Message}");
        }
    }
}
