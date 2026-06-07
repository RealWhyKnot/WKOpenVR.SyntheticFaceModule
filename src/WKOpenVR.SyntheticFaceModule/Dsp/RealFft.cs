namespace WKOpenVR.SyntheticFaceModule.Dsp;

/// <summary>
/// Radix-2 iterative FFT for real, windowed frames. Pure managed code (no external DSP
/// dependency); precomputes the Hann window, bit-reversal permutation, and twiddle factors so each
/// call is allocation-free. Produces the one-sided magnitude spectrum.
/// </summary>
public sealed class RealFft
{
    private readonly int _size;
    private readonly int[] _reverse;
    private readonly float[] _cos;
    private readonly float[] _sin;
    private readonly float[] _hann;
    private readonly float[] _re;
    private readonly float[] _im;

    public RealFft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0)
        {
            throw new ArgumentException("FFT size must be a power of two >= 2.", nameof(size));
        }

        _size = size;
        _re = new float[size];
        _im = new float[size];

        _hann = new float[size];
        for (int n = 0; n < size; n++)
        {
            _hann[n] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * n / (size - 1)));
        }

        int bits = (int)Math.Log2(size);
        _reverse = new int[size];
        for (int i = 0; i < size; i++)
        {
            int x = i;
            int r = 0;
            for (int b = 0; b < bits; b++)
            {
                r = (r << 1) | (x & 1);
                x >>= 1;
            }

            _reverse[i] = r;
        }

        _cos = new float[size / 2];
        _sin = new float[size / 2];
        for (int i = 0; i < size / 2; i++)
        {
            double angle = -2.0 * Math.PI * i / size;
            _cos[i] = (float)Math.Cos(angle);
            _sin[i] = (float)Math.Sin(angle);
        }
    }

    public int Size => _size;

    /// <summary>Number of bins in the one-sided spectrum (DC..Nyquist).</summary>
    public int SpectrumLength => (_size / 2) + 1;

    /// <summary>
    /// Computes the one-sided magnitude spectrum of <paramref name="samples"/> (zero-padded to the
    /// FFT size). When <paramref name="applyWindow"/> is true a Hann window is applied first.
    /// </summary>
    public void MagnitudeSpectrum(ReadOnlySpan<float> samples, float[] magnitude, bool applyWindow = true)
    {
        if (magnitude.Length < SpectrumLength)
        {
            throw new ArgumentException($"magnitude must hold at least {SpectrumLength} bins.", nameof(magnitude));
        }

        int n = _size;
        for (int i = 0; i < n; i++)
        {
            float s = i < samples.Length ? samples[i] : 0f;
            if (applyWindow)
            {
                s *= _hann[i];
            }

            int dst = _reverse[i];
            _re[dst] = s;
            _im[dst] = 0f;
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            int half = len >> 1;
            int step = n / len;
            for (int i = 0; i < n; i += len)
            {
                int k = 0;
                for (int j = i; j < i + half; j++)
                {
                    float wr = _cos[k];
                    float wi = _sin[k];
                    int m = j + half;
                    float tr = (wr * _re[m]) - (wi * _im[m]);
                    float ti = (wr * _im[m]) + (wi * _re[m]);
                    _re[m] = _re[j] - tr;
                    _im[m] = _im[j] - ti;
                    _re[j] += tr;
                    _im[j] += ti;
                    k += step;
                }
            }
        }

        int spec = SpectrumLength;
        for (int k = 0; k < spec; k++)
        {
            magnitude[k] = MathF.Sqrt((_re[k] * _re[k]) + (_im[k] * _im[k]));
        }
    }
}
