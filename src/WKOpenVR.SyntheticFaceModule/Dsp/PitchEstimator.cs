namespace WKOpenVR.SyntheticFaceModule.Dsp;

/// <summary>
/// Lightweight autocorrelation pitch tracker. Searches the lag range that corresponds to typical
/// human speech F0 and reports voicing from the normalized peak height. Cheap enough to run per
/// window; feeds prosody (F0 stats) and the voiced gate used by the viseme classifier.
/// </summary>
public sealed class PitchEstimator
{
    private readonly float _minHz;
    private readonly float _maxHz;
    private readonly float _voicedThreshold;

    public PitchEstimator(float minHz = 70f, float maxHz = 400f, float voicedThreshold = 0.30f)
    {
        _minHz = minHz;
        _maxHz = maxHz;
        _voicedThreshold = voicedThreshold;
    }

    public (float PitchHz, bool Voiced) Estimate(ReadOnlySpan<float> samples, int sampleRate)
    {
        int minLag = Math.Max(2, (int)(sampleRate / _maxHz));
        int maxLag = Math.Min(samples.Length - 1, (int)(sampleRate / _minHz));
        if (maxLag <= minLag)
        {
            return (0f, false);
        }

        double energy = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            energy += samples[i] * (double)samples[i];
        }

        if (energy < 1e-7)
        {
            return (0f, false);
        }

        double bestScore = 0;
        int bestLag = 0;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double corr = 0;
            int count = samples.Length - lag;
            for (int i = 0; i < count; i++)
            {
                corr += samples[i] * (double)samples[i + lag];
            }

            double normalized = corr / energy;
            if (normalized > bestScore)
            {
                bestScore = normalized;
                bestLag = lag;
            }
        }

        if (bestLag == 0 || bestScore < _voicedThreshold)
        {
            return (0f, false);
        }

        return (sampleRate / (float)bestLag, true);
    }
}
