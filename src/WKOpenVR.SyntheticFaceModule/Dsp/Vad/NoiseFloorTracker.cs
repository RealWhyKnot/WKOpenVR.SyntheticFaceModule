namespace WKOpenVR.SyntheticFaceModule.Dsp.Vad;

/// <summary>
/// Adaptive background-noise estimate. Tracks the RMS envelope downward quickly (to latch onto the
/// quietest recent level) and upward slowly (so steady fan/keyboard noise raises the floor without
/// speech dragging it up). The floor feeds the speech-activity thresholds.
/// </summary>
public sealed class NoiseFloorTracker
{
    private readonly float _trackDownSeconds;
    private readonly float _trackUpSeconds;
    private float _floor;

    public NoiseFloorTracker(float initialFloor = 1e-3f, float trackDownSeconds = 0.3f, float trackUpSeconds = 4.0f)
    {
        _floor = initialFloor;
        _trackDownSeconds = trackDownSeconds;
        _trackUpSeconds = trackUpSeconds;
    }

    public float Floor => _floor;

    public float Update(float rms, float dtSeconds)
    {
        float tau = rms < _floor ? _trackDownSeconds : _trackUpSeconds;
        float alpha = tau <= 0f || dtSeconds <= 0f ? 1f : 1f - MathF.Exp(-dtSeconds / tau);
        _floor += (rms - _floor) * alpha;
        if (_floor < 1e-6f)
        {
            _floor = 1e-6f;
        }

        return _floor;
    }

    public void Reset(float floor = 1e-3f) => _floor = floor;
}
