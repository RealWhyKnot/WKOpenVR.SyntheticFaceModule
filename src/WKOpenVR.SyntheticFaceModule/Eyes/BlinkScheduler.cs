namespace WKOpenVR.SyntheticFaceModule.Eyes;

/// <summary>
/// Generates natural-looking blinks. Inter-blink intervals are drawn from an exponential (hazard)
/// distribution around a target rate rather than a fixed period; each blink closes fast and opens
/// slower (matching measured human blink curves), with an occasional double-blink and a refractory
/// floor. Output is a single symmetric openness value (1 = open, 0 = closed) so the driver's
/// eyelid-sync passes it through unchanged. Deterministic given a seeded <see cref="Random"/>.
/// </summary>
public sealed class BlinkScheduler
{
    private enum Phase
    {
        Waiting,
        Closing,
        Holding,
        Opening,
    }

    private readonly Random _rng;
    private readonly float _meanIntervalSeconds;
    private readonly float _closeSeconds;
    private readonly float _holdSeconds;
    private readonly float _openSeconds;
    private readonly float _refractorySeconds;
    private readonly float _doubleBlinkProbability;

    private Phase _phase = Phase.Waiting;
    private float _timeToNext;
    private float _phaseTime;
    private float _openness = 1f;
    private bool _pendingDouble;

    public BlinkScheduler(
        Random rng,
        float blinksPerMinute = 15f,
        float closeSeconds = 0.06f,
        float holdSeconds = 0.02f,
        float openSeconds = 0.14f,
        float refractorySeconds = 1.5f,
        float doubleBlinkProbability = 0.15f)
    {
        _rng = rng;
        _meanIntervalSeconds = 60f / MathF.Max(1f, blinksPerMinute);
        _closeSeconds = MathF.Max(0.01f, closeSeconds);
        _holdSeconds = MathF.Max(0f, holdSeconds);
        _openSeconds = MathF.Max(0.01f, openSeconds);
        _refractorySeconds = refractorySeconds;
        _doubleBlinkProbability = doubleBlinkProbability;
        _timeToNext = SampleInterval();
    }

    /// <summary>Current eye openness, 1 = open, 0 = fully closed.</summary>
    public float Openness => _openness;

    public bool IsBlinking => _phase != Phase.Waiting;

    /// <summary>
    /// Advances the scheduler. <paramref name="arousal"/> (0..1) modestly raises blink frequency.
    /// Returns the current openness.
    /// </summary>
    public float Update(float dtSeconds, float arousal = 0f)
    {
        switch (_phase)
        {
            case Phase.Waiting:
                _openness = 1f;
                _timeToNext -= dtSeconds * (1f + (0.5f * Math.Clamp(arousal, 0f, 1f)));
                if (_timeToNext <= 0f)
                {
                    _phase = Phase.Closing;
                    _phaseTime = 0f;
                }

                break;

            case Phase.Closing:
                _phaseTime += dtSeconds;
                _openness = 1f - Math.Clamp(_phaseTime / _closeSeconds, 0f, 1f);
                if (_phaseTime >= _closeSeconds)
                {
                    _phase = Phase.Holding;
                    _phaseTime = 0f;
                    _openness = 0f;
                }

                break;

            case Phase.Holding:
                _openness = 0f;
                _phaseTime += dtSeconds;
                if (_phaseTime >= _holdSeconds)
                {
                    _phase = Phase.Opening;
                    _phaseTime = 0f;
                }

                break;

            case Phase.Opening:
                _phaseTime += dtSeconds;
                _openness = Math.Clamp(_phaseTime / _openSeconds, 0f, 1f);
                if (_phaseTime >= _openSeconds)
                {
                    _openness = 1f;
                    EndBlink();
                }

                break;
        }

        return _openness;
    }

    /// <summary>Nudge a blink to happen soon (e.g. on a gaze shift or speech pause).</summary>
    public void RequestBlinkSoon()
    {
        if (_phase == Phase.Waiting && _timeToNext > 0.12f)
        {
            _timeToNext = 0.1f;
        }
    }

    private void EndBlink()
    {
        _phase = Phase.Waiting;
        _phaseTime = 0f;

        if (_pendingDouble)
        {
            _pendingDouble = false;
            _timeToNext = 0.15f + ((float)_rng.NextDouble() * 0.20f);
        }
        else
        {
            _timeToNext = MathF.Max(_refractorySeconds, SampleInterval());
            if (_rng.NextDouble() < _doubleBlinkProbability)
            {
                _pendingDouble = true;
            }
        }
    }

    private float SampleInterval()
    {
        double u = 1.0 - _rng.NextDouble();
        float t = (float)(-_meanIntervalSeconds * Math.Log(u));
        return Math.Clamp(t, 0.8f, 3f * _meanIntervalSeconds);
    }
}
