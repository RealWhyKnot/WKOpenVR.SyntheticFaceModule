namespace WKOpenVR.SyntheticFaceModule.Eyes;

// Per-frame procedural eye state, applied to both eyes by the mixer.
public readonly record struct EyeOutput(
    float Openness,
    float GazeX,
    float GazeY,
    float PupilMm,
    float MinDilationMm,
    float MaxDilationMm);

// Blink scheduler, micro-saccade gaze and a slow pupil combined into one EyeOutput. Gaze shifts
// opportunistically nudge a blink, and eyelid openness couples mildly to downward gaze. The pupil
// drifts only with arousal, never per syllable. Deterministic given a seeded Random.
public sealed class ProceduralEyes
{
    private const float BasePupilMm = 4.0f;
    private const float MinPupilMm = 3.0f;
    private const float MaxPupilMm = 5.0f;

    // Half the pupil's arousal travel; full span stays inside the advertised 3-5 mm.
    private const float PupilArousalSwingMm = 0.8f;

    // Orbicularis oculi fires with 97% of saccadic gaze shifts larger than 33 degrees, and with
    // almost none of the small ones that make up seated conversation. Gaze here is normalized as
    // tan(angle) -- the driver folds it to (x, y, -1) -- so 33 degrees is tan(33) = 0.65. Gating on
    // amplitude rather than on a low per-saccade probability is what keeps this from becoming the
    // blink clock: saccades start ~130 times a minute.
    private const float BlinkSaccadeAmplitudeMin = 0.65f;
    private const float BlinkSaccadeProbability = 0.97f;

    // Occasional eyelid droop episodes: real lids rest at 1.0 almost always, but sag a
    // little every half minute or so instead of staying pinned open forever.
    private const float DroopEventsPerMinute = 2.0f;
    private const float DroopDepthMin = 0.05f;
    private const float DroopDepthMax = 0.15f;
    private const float DroopDurationMinSeconds = 2.0f;
    private const float DroopDurationMaxSeconds = 6.0f;

    // Eyes lead a head turn by up to tan(20 deg) horizontally before the counter-rotation pulls
    // them back; LeadRateScale converts the head's rad/s into that lead.
    private const float MaxLeadX = 0.364f;
    private const float MaxLeadY = 0.30f;
    private const float LeadRateScale = 0.25f;
    private const float LeadDwellSeconds = 0.3f;

    private const float HesitationAversionX = 0.30f;
    private const float HesitationAversionY = 0.12f;
    private const float HesitationDwellSeconds = 0.8f;

    private readonly Random _rng;
    private readonly BlinkScheduler _blink;
    private readonly MicroSaccadeGaze _gaze;
    private readonly Dsp.AsymmetricSmoother _pupil = new(attackSeconds: 1.5f, releaseSeconds: 2.5f, initial: BasePupilMm);

    private float _droopCountdown;
    private float _droopTime = -1f;
    private float _droopDuration;
    private float _droopDepth;

    public ProceduralEyes(Random rng)
    {
        _rng = rng;
        _blink = new BlinkScheduler(rng);
        _gaze = new MicroSaccadeGaze(rng);
        _droopCountdown = NextDroopGap();
    }

    public EyeOutput Update(
        float dtSeconds,
        float arousal = 0f,
        float blinksPerMinute = 15.9f,
        in EyeContext context = default)
    {
        _blink.BlinksPerMinute = blinksPerMinute;

        if (context.MotionOnset && !context.Asleep)
        {
            // The eyes arrive first; the counter-rotation rolls them back as the head lands.
            _gaze.LookAt(
                LeadOffset(context.HeadYawRate, MaxLeadX),
                _gaze.CenterY + LeadOffset(context.HeadPitchRate, MaxLeadY),
                LeadDwellSeconds);
        }

        if (context.Hesitation && !context.Asleep)
        {
            float side = _rng.NextDouble() < 0.5 ? -1f : 1f;
            _gaze.LookAt(side * HesitationAversionX, _gaze.CenterY + HesitationAversionY, HesitationDwellSeconds);
        }

        var drive = new GazeDrive(
            HeadYawRate: context.HeadYawRate,
            HeadPitchRate: context.HeadPitchRate,
            HeadMoving: context.HeadMoving || context.Asleep,
            Speaking: context.Speaking,
            SocialGaze: context.SocialGaze && !context.Asleep,
            VorGain: context.HeadValid ? context.VorGain : 0f,
            VorRecenterSeconds: context.VorRecenterSeconds);

        _gaze.Update(dtSeconds, arousal, drive);
        if (!context.Asleep
            && _gaze.SaccadeStarted
            && _gaze.SaccadeAmplitude >= BlinkSaccadeAmplitudeMin
            && _rng.NextDouble() < BlinkSaccadeProbability)
        {
            _blink.RequestBlinkSoon();
        }

        float openness = context.Asleep ? 1f : _blink.Update(dtSeconds, arousal);

        // Mild eyelid<->gaze coupling: looking down lowers the lids slightly.
        float downward = MathF.Max(0f, -_gaze.GazeY);
        openness *= 1f - (0.15f * downward);

        openness *= 1f - UpdateDroop(dtSeconds, arousal);
        openness *= 1f - Math.Clamp(context.LidClosure, 0f, 1f);

        float targetPupil = BasePupilMm + (PupilArousalSwingMm * (Math.Clamp(arousal, 0f, 1f) - 0.5f) * 2f);
        float pupil = _pupil.Update(targetPupil, dtSeconds);

        return new EyeOutput(
            Openness: Math.Clamp(openness, 0f, 1f),
            GazeX: _gaze.GazeX,
            GazeY: _gaze.GazeY,
            PupilMm: pupil,
            MinDilationMm: MinPupilMm,
            MaxDilationMm: MaxPupilMm);
    }

    private float UpdateDroop(float dtSeconds, float arousal)
    {
        if (_droopTime < 0f)
        {
            // High arousal suppresses sleepy lids.
            _droopCountdown -= dtSeconds * (1f - (0.7f * Math.Clamp(arousal, 0f, 1f)));
            if (_droopCountdown > 0f)
            {
                return 0f;
            }

            _droopTime = 0f;
            _droopDuration = Lerp(DroopDurationMinSeconds, DroopDurationMaxSeconds, (float)_rng.NextDouble());
            _droopDepth = Lerp(DroopDepthMin, DroopDepthMax, (float)_rng.NextDouble());
        }

        _droopTime += dtSeconds;
        if (_droopTime >= _droopDuration)
        {
            _droopTime = -1f;
            _droopCountdown = NextDroopGap();
            return 0f;
        }

        // Smooth half-sine envelope over the episode.
        float t = _droopTime / _droopDuration;
        return _droopDepth * MathF.Sin(t * MathF.PI);
    }

    private float NextDroopGap()
    {
        double u = Math.Max(1e-6, _rng.NextDouble());
        return (float)(-Math.Log(u) * 60.0 / DroopEventsPerMinute);
    }

    private static float LeadOffset(float rate, float maximum)
    {
        float magnitude = MathF.Min(MathF.Abs(rate) * LeadRateScale, maximum);
        return rate >= 0f ? magnitude : -magnitude;
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}
