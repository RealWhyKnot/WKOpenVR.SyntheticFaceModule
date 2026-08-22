namespace WKOpenVR.SyntheticFaceModule.Eyes;

// Procedural gaze: hold a fixation for a short dwell, then a fast ballistic saccade to a new target
// in a small social cone, with micro-drift during fixation. Output is conjugate and normalized to
// [-1, 1], which the driver folds to (x, y, -1), so a value is tan(angle), not the angle.
// Deterministic given a seeded Random.
public sealed class MicroSaccadeGaze
{
    // tan(30 deg): the eye stays well inside its comfortable orbital range.
    private const float MaxVor = 0.577f;

    // Radius of the "looking at a face" disc around the cone centre.
    public const float FaceConeRadius = 0.12f;

    private const float FaceGazeWhileSpeaking = 0.40f;
    private const float FaceGazeWhileListening = 0.75f;
    private const float ListeningDwellScale = 1.6f;

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
    private float _vorX;
    private float _vorY;
    private float _forcedDwell = -1f;
    private bool _wasSpeaking;
    private bool _socialGaze;
    private bool _speaking;

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

    public float GazeX { get; private set; }

    public float GazeY { get; private set; }

    public float CenterY => _centerY;

    // True on the frame a new saccade begins, so a blink can be nudged.
    public bool SaccadeStarted { get; private set; }

    public float SaccadeAmplitude { get; private set; }

    // Counter-rotation currently applied on top of the fixation, in gaze units.
    public float VorX => _vorX;

    public float VorY => _vorY;

    // arousal (0..1) shortens dwell times.
    public void Update(float dtSeconds, float arousal = 0f, in GazeDrive drive = default)
    {
        SaccadeStarted = false;
        _socialGaze = drive.SocialGaze;
        _speaking = drive.Speaking;

        UpdateVor(dtSeconds, drive);

        if (drive.SocialGaze && drive.Speaking != _wasSpeaking)
        {
            // Speakers look away as they start talking and back as they hand the turn over.
            if (drive.Speaking)
            {
                float side = _rng.NextDouble() < 0.5 ? -1f : 1f;
                LookAt(side * 0.28f, _centerY, 0.5f);
            }
            else
            {
                LookAt(0f, _centerY, 0.6f);
            }
        }

        _wasSpeaking = drive.Speaking;

        if (_phase == Phase.Fixating)
        {
            _dwellRemaining -= dtSeconds;

            // Bounded random-walk micro-drift around the fixation point.
            _driftX += ((float)_rng.NextDouble() - 0.5f) * _driftAmplitude * dtSeconds * 8f;
            _driftY += ((float)_rng.NextDouble() - 0.5f) * _driftAmplitude * dtSeconds * 8f;
            _driftX = Math.Clamp(_driftX, -_driftAmplitude, _driftAmplitude);
            _driftY = Math.Clamp(_driftY, -_driftAmplitude, _driftAmplitude);

            GazeX = Math.Clamp(_baseX + _driftX + _vorX, -1f, 1f);
            GazeY = Math.Clamp(_baseY + _driftY + _vorY, -1f, 1f);

            // A swinging head folds its own gaze shift in; new idle saccades wait for it to settle.
            if (_dwellRemaining <= 0f && !drive.HeadMoving)
            {
                BeginSaccade(arousal);
            }
        }
        else
        {
            _saccadeTime += dtSeconds;
            float t = _saccadeDuration <= 0f ? 1f : Math.Clamp(_saccadeTime / _saccadeDuration, 0f, 1f);
            float eased = EaseInOut(t);
            GazeX = Math.Clamp(Lerp(_startX, _targetX, eased) + _vorX, -1f, 1f);
            GazeY = Math.Clamp(Lerp(_startY, _targetY, eased) + _vorY, -1f, 1f);

            if (t >= 1f)
            {
                _phase = Phase.Fixating;
                _baseX = _targetX;
                _baseY = _targetY;
                _driftX = 0f;
                _driftY = 0f;
                _dwellRemaining = NextDwell(arousal);
            }
        }
    }

    // Sends the eyes to an explicit point, used for gaze aversion and to lead a head turn.
    // Ignored mid-saccade so a ballistic movement is never cut in half.
    public void LookAt(float x, float y, float dwellSeconds)
    {
        if (_phase != Phase.Fixating)
        {
            return;
        }

        _targetX = Math.Clamp(x, -1f, 1f);
        _targetY = Math.Clamp(y, -1f, 1f);
        _forcedDwell = dwellSeconds;
        LaunchSaccade();
    }

    private void UpdateVor(float dtSeconds, in GazeDrive drive)
    {
        if (drive.VorGain <= 0f)
        {
            _vorX = 0f;
            _vorY = 0f;
            return;
        }

        // Head left (positive yaw rate) means the eyes must travel right to hold the target.
        // The (1 + v*v) factor converts an angular rate into this tangent-space gaze unit.
        _vorX += drive.VorGain * drive.HeadYawRate * dtSeconds * (1f + (_vorX * _vorX));
        _vorY -= drive.VorGain * drive.HeadPitchRate * dtSeconds * (1f + (_vorY * _vorY));
        _vorX = Math.Clamp(_vorX, -MaxVor, MaxVor);
        _vorY = Math.Clamp(_vorY, -MaxVor, MaxVor);

        if (!drive.HeadMoving)
        {
            float recenter = MathF.Max(0.01f, drive.VorRecenterSeconds);
            float a = 1f - MathF.Exp(-MathF.Max(0f, dtSeconds) / recenter);
            _vorX -= _vorX * a;
            _vorY -= _vorY * a;
        }
    }

    private void BeginSaccade(float arousal)
    {
        if (_socialGaze)
        {
            float faceProbability = _speaking ? FaceGazeWhileSpeaking : FaceGazeWhileListening;
            if (_rng.NextDouble() < faceProbability)
            {
                // Somewhere on the listener's face, not a pinpoint stare at one eye.
                double angle = _rng.NextDouble() * 2.0 * Math.PI;
                float radius = FaceConeRadius * MathF.Sqrt((float)_rng.NextDouble());
                _targetX = radius * (float)Math.Cos(angle);
                _targetY = _centerY + (radius * (float)Math.Sin(angle));
                LaunchSaccade();
                return;
            }

            DrawConeTarget(arousal);

            // Keep away-glances clear of the face disc so the two states stay distinguishable.
            float offFaceMin = FaceConeRadius + 0.02f;
            if (MathF.Abs(_targetX) < offFaceMin)
            {
                _targetX = _targetX >= 0f ? offFaceMin : -offFaceMin;
            }

            LaunchSaccade();
            return;
        }

        DrawConeTarget(arousal);
        LaunchSaccade();
    }

    private void DrawConeTarget(float arousal)
    {
        // Bias toward the social center; arousal makes wider glances likelier and larger.
        float a = Math.Clamp(arousal, 0f, 1f);
        float reach = _rng.NextDouble() < 0.15 + (0.25 * a) ? 1.0f : 0.55f + (0.2f * a);
        _targetX = ((float)(_rng.NextDouble() * 2.0 - 1.0)) * _coneX * reach;
        float spanY = (float)(_rng.NextDouble() * 2.0 - 1.0);
        _targetY = _centerY + (spanY * (spanY >= 0f ? _coneYUp : _coneYDown) * reach);
    }

    private void LaunchSaccade()
    {
        _startX = GazeX - _vorX;
        _startY = GazeY - _vorY;

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

    private float NextDwell(float arousal)
    {
        if (_forcedDwell >= 0f)
        {
            float forced = _forcedDwell;
            _forcedDwell = -1f;
            return forced;
        }

        float dwell = SampleDwell(arousal);
        if (_socialGaze && !_speaking)
        {
            // Listeners hold the speaker's face longer than they hold an idle glance.
            dwell = Math.Clamp(dwell * ListeningDwellScale, _minDwellSeconds, _maxDwellSeconds);
        }

        return dwell;
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
