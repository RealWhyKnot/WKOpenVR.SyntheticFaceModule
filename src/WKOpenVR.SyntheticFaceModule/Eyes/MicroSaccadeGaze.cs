namespace WKOpenVR.SyntheticFaceModule.Eyes;

/// <summary>
/// Procedural gaze: the eye holds a fixation for a short dwell, then makes a fast ballistic saccade
/// to a new target within a small social cone, with tiny micro-drift superimposed during fixation.
/// Output is conjugate (both eyes share it). Coordinates are normalized to [-1, 1]. Deterministic
/// given a seeded <see cref="Random"/>.
/// </summary>
public sealed class MicroSaccadeGaze
{
    private enum Phase
    {
        Fixating,
        Saccading,
    }

    private readonly Random _rng;
    private readonly float _coneX;
    private readonly float _coneYUp;
    private readonly float _coneYDown;
    private readonly float _centerY;
    private readonly float _driftAmplitude;
    private readonly float _minDwellSeconds;
    private readonly float _dwellMedianSeconds;
    private readonly float _dwellSigma;
    private readonly float _maxDwellSeconds;

    private Phase _phase = Phase.Fixating;
    private float _dwellRemaining;
    private float _saccadeTime;
    private float _saccadeDuration;
    private float _startX;
    private float _startY;
    private float _targetX;
    private float _targetY;
    private float _baseX;
    private float _baseY;
    private float _driftX;
    private float _driftY;

    // Defaults pooled from 15 tracked sessions: gaze rides slightly below center, fixations
    // have a 233 ms median with a long log-normal tail (about 102 saccades a minute), not the
    // leisurely seconds-long dwells of an idle loop.
    public MicroSaccadeGaze(
        Random rng,
        float coneX = 0.35f,
        float coneYUp = 0.30f,
        float coneYDown = 0.30f,
        float centerY = -0.13f,
        float driftAmplitude = 0.02f,
        float minDwellSeconds = 0.08f,
        float dwellMedianSeconds = 0.233f,
        float dwellSigma = 1.3f,
        float maxDwellSeconds = 1.5f)
    {
        _rng = rng;
        _coneX = coneX;
        _coneYUp = coneYUp;
        _coneYDown = coneYDown;
        _centerY = centerY;
        _driftAmplitude = driftAmplitude;
        _minDwellSeconds = minDwellSeconds;
        _dwellMedianSeconds = dwellMedianSeconds;
        _dwellSigma = dwellSigma;
        _maxDwellSeconds = maxDwellSeconds;
        _baseY = centerY;
        GazeY = centerY;
        _dwellRemaining = SampleDwell(0f);
    }

    /// <summary>Horizontal gaze, normalized [-1, 1].</summary>
    public float GazeX { get; private set; }

    /// <summary>Vertical gaze, normalized [-1, 1].</summary>
    public float GazeY { get; private set; }

    /// <summary>True at the instant a new saccade begins (useful to nudge a blink).</summary>
    public bool SaccadeStarted { get; private set; }

    public float SaccadeAmplitude { get; private set; }

    /// <summary>Advances gaze. <paramref name="arousal"/> (0..1) shortens dwell times.</summary>
    public void Update(float dtSeconds, float arousal = 0f)
    {
        SaccadeStarted = false;

        if (_phase == Phase.Fixating)
        {
            _dwellRemaining -= dtSeconds;

            // Bounded random-walk micro-drift around the fixation point.
            _driftX += ((float)_rng.NextDouble() - 0.5f) * _driftAmplitude * dtSeconds * 8f;
            _driftY += ((float)_rng.NextDouble() - 0.5f) * _driftAmplitude * dtSeconds * 8f;
            _driftX = Math.Clamp(_driftX, -_driftAmplitude, _driftAmplitude);
            _driftY = Math.Clamp(_driftY, -_driftAmplitude, _driftAmplitude);

            GazeX = Math.Clamp(_baseX + _driftX, -1f, 1f);
            GazeY = Math.Clamp(_baseY + _driftY, -1f, 1f);

            if (_dwellRemaining <= 0f)
            {
                BeginSaccade(arousal);
            }
        }
        else
        {
            _saccadeTime += dtSeconds;
            float t = _saccadeDuration <= 0f ? 1f : Math.Clamp(_saccadeTime / _saccadeDuration, 0f, 1f);
            float eased = EaseInOut(t);
            GazeX = Math.Clamp(Lerp(_startX, _targetX, eased), -1f, 1f);
            GazeY = Math.Clamp(Lerp(_startY, _targetY, eased), -1f, 1f);

            if (t >= 1f)
            {
                _phase = Phase.Fixating;
                _baseX = _targetX;
                _baseY = _targetY;
                _driftX = 0f;
                _driftY = 0f;
                _dwellRemaining = SampleDwell(arousal);
            }
        }
    }

    private void BeginSaccade(float arousal)
    {
        _startX = GazeX;
        _startY = GazeY;

        // Bias toward the social center; arousal makes wider glances likelier and larger.
        float a = Math.Clamp(arousal, 0f, 1f);
        float reach = _rng.NextDouble() < 0.15 + (0.25 * a) ? 1.0f : 0.55f + (0.2f * a);
        _targetX = ((float)(_rng.NextDouble() * 2.0 - 1.0)) * _coneX * reach;
        float spanY = (float)(_rng.NextDouble() * 2.0 - 1.0);
        _targetY = _centerY + (spanY * (spanY >= 0f ? _coneYUp : _coneYDown) * reach);

        float distance = MathF.Sqrt(
            ((_targetX - _startX) * (_targetX - _startX)) +
            ((_targetY - _startY) * (_targetY - _startY)));

        // Main-sequence-style: larger amplitude -> longer (still tens of ms).
        _saccadeDuration = 0.025f + (0.045f * distance);
        _saccadeTime = 0f;
        _phase = Phase.Saccading;
        SaccadeStarted = true;
        SaccadeAmplitude = distance;
    }

    private float SampleDwell(float arousal)
    {
        // Log-normal dwell: tracked fixations have a 233 ms median but a mean near 550 ms and a
        // p90 past a second, which an exponential cannot hold at once. Arousal shortens the median.
        float median = _dwellMedianSeconds * (1f - (0.5f * Math.Clamp(arousal, 0f, 1f)));
        double u1 = Math.Max(1e-9, _rng.NextDouble());
        double u2 = _rng.NextDouble();
        double gaussian = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        float dwell = median * MathF.Exp(_dwellSigma * (float)gaussian);
        return Math.Clamp(dwell, _minDwellSeconds, _maxDwellSeconds);
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static float EaseInOut(float t) => t * t * (3f - (2f * t));
}
