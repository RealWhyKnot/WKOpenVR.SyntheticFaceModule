namespace WKOpenVR.SyntheticFaceModule.Prosody;

/// <summary>
/// Per-speaker running baseline: an exponentially-weighted mean and variance of a feature, used to
/// produce z-scores. Driving expression from z-scores (relative to the speaker's own norm) is far
/// more robust than fixed universal thresholds across mics, voices, and gain.
/// </summary>
public sealed class RunningBaseline
{
    private readonly float _tauSeconds;
    private double _mean;
    private double _variance;
    private bool _initialized;

    public RunningBaseline(float tauSeconds = 20f)
    {
        _tauSeconds = tauSeconds;
    }

    public float Mean => (float)_mean;

    /// <summary>Updates the baseline with a sample and returns its z-score against the running stats.</summary>
    public float Update(float value, float dtSeconds)
    {
        if (!_initialized)
        {
            _mean = value;
            _variance = 1e-4;
            _initialized = true;
            return 0f;
        }

        float alpha = dtSeconds <= 0f ? 0f : 1f - MathF.Exp(-dtSeconds / _tauSeconds);
        double delta = value - _mean;
        _mean += alpha * delta;
        _variance = ((1f - alpha) * (_variance + (alpha * delta * delta)));

        double std = Math.Sqrt(Math.Max(_variance, 1e-6));
        return (float)((value - _mean) / std);
    }

    public void Reset()
    {
        _initialized = false;
        _mean = 0;
        _variance = 0;
    }
}
