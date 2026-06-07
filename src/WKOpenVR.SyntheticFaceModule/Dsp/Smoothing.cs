namespace WKOpenVR.SyntheticFaceModule.Dsp;

/// <summary>
/// Frame-rate-independent asymmetric one-pole smoother. Attack and release time constants are in
/// seconds; the per-step coefficient is derived from the elapsed time so behavior is identical at
/// any update rate. Use a short attack and longer release for natural mouth/expression motion.
/// </summary>
public sealed class AsymmetricSmoother
{
    private readonly float _attackSeconds;
    private readonly float _releaseSeconds;
    private float _value;

    public AsymmetricSmoother(float attackSeconds, float releaseSeconds, float initial = 0f)
    {
        _attackSeconds = MathF.Max(0f, attackSeconds);
        _releaseSeconds = MathF.Max(0f, releaseSeconds);
        _value = initial;
    }

    public float Value => _value;

    public float Update(float target, float dtSeconds)
    {
        float tau = target > _value ? _attackSeconds : _releaseSeconds;
        float alpha = tau <= 0f || dtSeconds <= 0f ? 1f : 1f - MathF.Exp(-dtSeconds / tau);
        _value += (target - _value) * alpha;
        return _value;
    }

    public void Reset(float value = 0f) => _value = value;
}
