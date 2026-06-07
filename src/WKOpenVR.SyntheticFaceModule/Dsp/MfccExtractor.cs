namespace WKOpenVR.SyntheticFaceModule.Dsp;

/// <summary>
/// Computes mel-frequency cepstral coefficients from a magnitude spectrum: a triangular mel
/// filterbank over the power spectrum, log compression, then a DCT-II. This is a from-scratch
/// reimplementation of the standard MFCC pipeline (the same algorithm family uLipSync uses); it is
/// not a port of any third-party asset. Allocation-free per call.
/// </summary>
public sealed class MfccExtractor
{
    private readonly int _melCount;
    private readonly int _mfccCount;
    private readonly int _spectrumLength;
    private readonly float[][] _melWeights;
    private readonly float[,] _dct;
    private readonly float[] _melEnergies;

    public MfccExtractor(int sampleRate, int fftSize, int melCount, int mfccCount, float fMin = 80f, float fMax = 0f)
    {
        if (melCount < mfccCount)
        {
            throw new ArgumentException("melCount must be >= mfccCount.", nameof(melCount));
        }

        _melCount = melCount;
        _mfccCount = mfccCount;
        _spectrumLength = (fftSize / 2) + 1;
        _melEnergies = new float[melCount];

        if (fMax <= 0f || fMax > sampleRate / 2f)
        {
            fMax = sampleRate / 2f;
        }

        _melWeights = BuildMelFilterbank(sampleRate, fftSize, melCount, fMin, fMax);

        _dct = new float[mfccCount, melCount];
        float scale = MathF.Sqrt(2f / melCount);
        for (int i = 0; i < mfccCount; i++)
        {
            for (int m = 0; m < melCount; m++)
            {
                _dct[i, m] = scale * MathF.Cos(MathF.PI * i * (m + 0.5f) / melCount);
            }
        }
    }

    public int MfccCount => _mfccCount;

    /// <summary>Computes MFCCs from a one-sided magnitude spectrum into <paramref name="mfccOut"/>.</summary>
    public void Compute(ReadOnlySpan<float> magnitude, float[] mfccOut)
    {
        if (mfccOut.Length < _mfccCount)
        {
            throw new ArgumentException($"mfccOut must hold at least {_mfccCount} coefficients.", nameof(mfccOut));
        }

        for (int m = 0; m < _melCount; m++)
        {
            float[] weights = _melWeights[m];
            float sum = 0f;
            for (int k = 0; k < _spectrumLength; k++)
            {
                float w = weights[k];
                if (w > 0f)
                {
                    float power = magnitude[k] * magnitude[k];
                    sum += w * power;
                }
            }

            _melEnergies[m] = MathF.Log(sum + 1e-10f);
        }

        for (int i = 0; i < _mfccCount; i++)
        {
            float acc = 0f;
            for (int m = 0; m < _melCount; m++)
            {
                acc += _dct[i, m] * _melEnergies[m];
            }

            mfccOut[i] = acc;
        }
    }

    private static float[][] BuildMelFilterbank(int sampleRate, int fftSize, int melCount, float fMin, float fMax)
    {
        int spectrumLength = (fftSize / 2) + 1;
        float melMin = HzToMel(fMin);
        float melMax = HzToMel(fMax);

        // melCount+2 boundary points -> melCount triangular filters.
        float[] centersHz = new float[melCount + 2];
        for (int i = 0; i < centersHz.Length; i++)
        {
            float mel = melMin + ((melMax - melMin) * i / (melCount + 1));
            centersHz[i] = MelToHz(mel);
        }

        float binHz = sampleRate / (float)fftSize;
        float[][] weights = new float[melCount][];
        for (int m = 0; m < melCount; m++)
        {
            float left = centersHz[m];
            float center = centersHz[m + 1];
            float right = centersHz[m + 2];
            float[] row = new float[spectrumLength];
            for (int k = 0; k < spectrumLength; k++)
            {
                float hz = k * binHz;
                float w;
                if (hz < left || hz > right)
                {
                    w = 0f;
                }
                else if (hz <= center)
                {
                    w = center > left ? (hz - left) / (center - left) : 0f;
                }
                else
                {
                    w = right > center ? (right - hz) / (right - center) : 0f;
                }

                row[k] = w < 0f ? 0f : w;
            }

            weights[m] = row;
        }

        return weights;
    }

    private static float HzToMel(float hz) => 2595f * MathF.Log10(1f + (hz / 700f));

    private static float MelToHz(float mel) => 700f * (MathF.Pow(10f, mel / 2595f) - 1f);
}
