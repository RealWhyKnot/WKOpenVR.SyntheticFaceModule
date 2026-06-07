using WKOpenVR.SyntheticFaceModule.Dsp;

namespace WKOpenVR.SyntheticFaceModule.Audio;

/// <summary>
/// Turns a window of mono PCM samples into an <see cref="AudioAnalysisFrame"/> (RMS, ZCR, spectral
/// centroid/rolloff/flux, MFCC, pitch/voiced). Owns reusable buffers so analysis is allocation-free.
/// </summary>
public sealed class AudioAnalyzer
{
    private readonly int _sampleRate;
    private readonly int _fftSize;
    private readonly RealFft _fft;
    private readonly MfccExtractor _mfcc;
    private readonly PitchEstimator _pitch;
    private readonly float[] _magnitude;
    private readonly float[] _previousMagnitude;
    private bool _hasPrevious;

    public AudioAnalyzer(int sampleRate, int fftSize = 512, int melCount = 26, int mfccCount = 13)
    {
        _sampleRate = sampleRate;
        _fftSize = fftSize;
        _fft = new RealFft(fftSize);
        _mfcc = new MfccExtractor(sampleRate, fftSize, melCount, mfccCount);
        _pitch = new PitchEstimator();
        _magnitude = new float[_fft.SpectrumLength];
        _previousMagnitude = new float[_fft.SpectrumLength];
    }

    public int MfccCount => _mfcc.MfccCount;

    public void Analyze(ReadOnlySpan<float> window, double timestampSeconds, float durationSeconds, AudioAnalysisFrame outFrame)
    {
        _fft.MagnitudeSpectrum(window, _magnitude);

        outFrame.SampleRate = _sampleRate;
        outFrame.TimestampSeconds = timestampSeconds;
        outFrame.DurationSeconds = durationSeconds;
        outFrame.Rms = SpectralFeatures.Rms(window);
        outFrame.ZeroCrossingRate = SpectralFeatures.ZeroCrossingRate(window);
        outFrame.SpectralCentroidHz = SpectralFeatures.Centroid(_magnitude, _sampleRate, _fftSize);
        outFrame.SpectralRolloffHz = SpectralFeatures.Rolloff(_magnitude, _sampleRate, _fftSize, 0.85f);
        outFrame.SpectralFlux = _hasPrevious ? SpectralFeatures.Flux(_magnitude, _previousMagnitude) : 0f;

        _mfcc.Compute(_magnitude, outFrame.Mfcc);

        (float pitchHz, bool voiced) = _pitch.Estimate(window, _sampleRate);
        outFrame.PitchHz = pitchHz;
        outFrame.Voiced = voiced;

        Array.Copy(_magnitude, _previousMagnitude, _magnitude.Length);
        _hasPrevious = true;
    }

    public void Reset() => _hasPrevious = false;
}
