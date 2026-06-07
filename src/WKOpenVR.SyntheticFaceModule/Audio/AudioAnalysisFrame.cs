namespace WKOpenVR.SyntheticFaceModule.Audio;

/// <summary>
/// A snapshot of per-window audio features handed from the capture/analysis thread to the
/// per-frame update loop. One instance is produced per analysis window; consumers read it without
/// locking once published.
/// </summary>
public sealed class AudioAnalysisFrame
{
    public AudioAnalysisFrame(int mfccCount)
    {
        Mfcc = new float[mfccCount];
    }

    /// <summary>Monotonic capture time of this window, in seconds.</summary>
    public double TimestampSeconds { get; set; }

    /// <summary>Window duration in seconds.</summary>
    public float DurationSeconds { get; set; }

    public int SampleRate { get; set; }

    /// <summary>Root-mean-square level of the window, linear (roughly 0..1).</summary>
    public float Rms { get; set; }

    /// <summary>Zero-crossing rate, 0..1 (fraction of adjacent-sample sign changes).</summary>
    public float ZeroCrossingRate { get; set; }

    /// <summary>Spectral centroid in Hz (the spectrum's "center of mass").</summary>
    public float SpectralCentroidHz { get; set; }

    /// <summary>Frequency below which a fraction (default 85%) of spectral energy lies, in Hz.</summary>
    public float SpectralRolloffHz { get; set; }

    /// <summary>Positive spectral flux versus the previous window (onset/transient strength).</summary>
    public float SpectralFlux { get; set; }

    /// <summary>Estimated fundamental frequency in Hz, or 0 when unvoiced.</summary>
    public float PitchHz { get; set; }

    /// <summary>True when the window is voiced (periodic).</summary>
    public bool Voiced { get; set; }

    /// <summary>Mel-frequency cepstral coefficients for this window.</summary>
    public float[] Mfcc { get; }

    public void CopyFrom(AudioAnalysisFrame other)
    {
        TimestampSeconds = other.TimestampSeconds;
        DurationSeconds = other.DurationSeconds;
        SampleRate = other.SampleRate;
        Rms = other.Rms;
        ZeroCrossingRate = other.ZeroCrossingRate;
        SpectralCentroidHz = other.SpectralCentroidHz;
        SpectralRolloffHz = other.SpectralRolloffHz;
        SpectralFlux = other.SpectralFlux;
        PitchHz = other.PitchHz;
        Voiced = other.Voiced;
        int n = Math.Min(Mfcc.Length, other.Mfcc.Length);
        Array.Copy(other.Mfcc, Mfcc, n);
    }
}
