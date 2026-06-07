namespace WKOpenVR.SyntheticFaceModule.Dsp;

/// <summary>Stateless spectral/time-domain feature helpers. Pure functions, easy to unit test.</summary>
public static class SpectralFeatures
{
    public static float Rms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
        {
            return 0f;
        }

        double sum = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * (double)samples[i];
        }

        return (float)Math.Sqrt(sum / samples.Length);
    }

    public static float ZeroCrossingRate(ReadOnlySpan<float> samples)
    {
        if (samples.Length < 2)
        {
            return 0f;
        }

        int crossings = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            bool prevNeg = samples[i - 1] < 0f;
            bool curNeg = samples[i] < 0f;
            if (prevNeg != curNeg)
            {
                crossings++;
            }
        }

        return crossings / (float)(samples.Length - 1);
    }

    /// <summary>Spectral centroid (Hz): the magnitude-weighted mean frequency.</summary>
    public static float Centroid(ReadOnlySpan<float> magnitude, int sampleRate, int fftSize)
    {
        float binHz = sampleRate / (float)fftSize;
        double weighted = 0;
        double total = 0;
        for (int k = 0; k < magnitude.Length; k++)
        {
            float m = magnitude[k];
            weighted += k * binHz * (double)m;
            total += m;
        }

        return total > 1e-9 ? (float)(weighted / total) : 0f;
    }

    /// <summary>Frequency (Hz) below which <paramref name="fraction"/> of the energy lies.</summary>
    public static float Rolloff(ReadOnlySpan<float> magnitude, int sampleRate, int fftSize, float fraction = 0.85f)
    {
        float binHz = sampleRate / (float)fftSize;
        double total = 0;
        for (int k = 0; k < magnitude.Length; k++)
        {
            total += magnitude[k];
        }

        if (total <= 1e-9)
        {
            return 0f;
        }

        double threshold = total * fraction;
        double running = 0;
        for (int k = 0; k < magnitude.Length; k++)
        {
            running += magnitude[k];
            if (running >= threshold)
            {
                return k * binHz;
            }
        }

        return (magnitude.Length - 1) * binHz;
    }

    /// <summary>
    /// Positive spectral flux versus the previous magnitude spectrum (sum of positive bin-to-bin
    /// increases, normalized by bin count). Returns 0 when there is no previous spectrum.
    /// </summary>
    public static float Flux(ReadOnlySpan<float> magnitude, ReadOnlySpan<float> previousMagnitude)
    {
        if (previousMagnitude.Length != magnitude.Length || magnitude.Length == 0)
        {
            return 0f;
        }

        double sum = 0;
        for (int k = 0; k < magnitude.Length; k++)
        {
            float diff = magnitude[k] - previousMagnitude[k];
            if (diff > 0f)
            {
                sum += diff;
            }
        }

        return (float)(sum / magnitude.Length);
    }
}
