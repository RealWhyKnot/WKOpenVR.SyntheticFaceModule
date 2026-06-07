namespace WKOpenVR.SyntheticFaceModule.Eyes;

/// <summary>Per-frame procedural eye state, applied symmetrically to both eyes by the mixer.</summary>
public readonly record struct EyeOutput(
    float Openness,
    float GazeX,
    float GazeY,
    float PupilMm,
    float MinDilationMm,
    float MaxDilationMm);

/// <summary>
/// Combines the blink scheduler, micro-saccade gaze, and a near-constant pupil into a symmetric
/// <see cref="EyeOutput"/>. Gaze shifts opportunistically nudge a blink (as in natural viewing), and
/// eyelid openness couples mildly to downward gaze. Pupil drifts only slowly with arousal - never
/// per syllable. Deterministic given a seeded <see cref="Random"/>.
/// </summary>
public sealed class ProceduralEyes
{
    private const float BasePupilMm = 4.0f;
    private const float MinPupilMm = 3.0f;
    private const float MaxPupilMm = 5.0f;

    private readonly BlinkScheduler _blink;
    private readonly MicroSaccadeGaze _gaze;
    private readonly Dsp.AsymmetricSmoother _pupil = new(attackSeconds: 1.5f, releaseSeconds: 2.5f, initial: BasePupilMm);

    public ProceduralEyes(Random rng)
    {
        _blink = new BlinkScheduler(rng);
        _gaze = new MicroSaccadeGaze(rng);
    }

    public EyeOutput Update(float dtSeconds, float arousal = 0f)
    {
        _gaze.Update(dtSeconds, arousal);
        if (_gaze.SaccadeStarted)
        {
            _blink.RequestBlinkSoon();
        }

        float openness = _blink.Update(dtSeconds, arousal);

        // Mild eyelid<->gaze coupling: looking down lowers the lids slightly.
        float downward = MathF.Max(0f, -_gaze.GazeY);
        openness *= 1f - (0.15f * downward);

        float targetPupil = BasePupilMm + (0.2f * (Math.Clamp(arousal, 0f, 1f) - 0.5f) * 2f);
        float pupil = _pupil.Update(targetPupil, dtSeconds);

        return new EyeOutput(
            Openness: Math.Clamp(openness, 0f, 1f),
            GazeX: _gaze.GazeX,
            GazeY: _gaze.GazeY,
            PupilMm: pupil,
            MinDilationMm: MinPupilMm,
            MaxDilationMm: MaxPupilMm);
    }
}
