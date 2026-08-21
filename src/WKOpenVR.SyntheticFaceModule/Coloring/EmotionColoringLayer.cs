using WKOpenVR.FaceTracking.Sdk;
using WKOpenVR.SyntheticFaceModule.Prosody;

namespace WKOpenVR.SyntheticFaceModule.Coloring;

// Maps prosody to additive expression offsets. Two tiers: subtle arousal-driven brow/eye coloring
// with hard low caps, and an episodic smile on the mouth corners.
//
// Smile is deliberately NOT driven by valence. Valence recovery from audio alone sits near chance
// while arousal is recoverable, so a valence-tracking smile just follows vocal timbre: brighter
// sibilants read as happy, loud speech reads as sad. Instead the corners run as discrete episodes
// whose rate scales with speech and arousal, shaped to the timings measured from hardware
// recordings (0.7 episodes/min, ~0.55 s onset, ~5.3 s duration, ~2.3 s offset, peaks near 1.0).
//
// The layer never writes viseme-critical shapes: jaw, MouthClosed, funnel, pucker, stretch, and the
// upper/lower lip openers stay owned by the audio mouth solver.
public sealed class EmotionColoringLayer
{
    private const float ConfidenceGate = 0.25f;
    private const float AttackSeconds = 0.2f;
    private const float DecaySeconds = 1.5f;

    private const float EpisodesPerMinute = 0.7f;
    private const float OnsetSeconds = 0.55f;
    private const float OffsetSeconds = 2.3f;
    private const float DurationMinSeconds = 3.0f;
    private const float DurationMaxSeconds = 8.0f;
    private const float PeakMin = 0.50f;
    private const float PeakMax = 1.00f;

    private const float DimpleRatio = 0.37f;
    private const float CheekSmileRatio = 0.55f;
    private const float EyeSquintSmileRatio = 0.35f;

    private static readonly FaceExpression[] SubtleShapes =
    {
        FaceExpression.BrowOuterUpRight,
        FaceExpression.BrowOuterUpLeft,
        FaceExpression.EyeWideRight,
        FaceExpression.EyeWideLeft,
    };

    private readonly Random _rng;
    private readonly float[] _smoothed = new float[FaceExpressionCount.Value];
    private readonly float[] _target = new float[FaceExpressionCount.Value];

    private float _secondsToNextEpisode = -1f;
    private float _secondsIntoEpisode = -1f;
    private float _episodeDuration;
    private float _episodePeak;

    public EmotionColoringLayer(Random rng)
    {
        _rng = rng;
    }

    // Diagnostics: the smile envelope produced this frame, after intensity scaling.
    public float LastSmileEnvelope { get; private set; }

    // Clears offsets and writes the additive coloring into it.
    public void Apply(ProsodyState prosody, float intensity, float smileIntensity, float dtSeconds, float[] offsets)
    {
        Array.Clear(offsets);
        Array.Clear(_target);

        // Gated on confidence only. Gating on SpeechActive as well put a cliff at every pause: the
        // whole face snapped toward neutral the moment the mic went quiet. Confidence already decays.
        float gate = prosody.Confidence >= ConfidenceGate
            ? prosody.Confidence * Math.Clamp(intensity, 0f, 1f)
            : 0f;

        float arousalHigh = Math.Clamp((prosody.Arousal - 0.5f) * 2f, 0f, 1f);

        SetTarget(FaceExpression.BrowOuterUpRight, gate * arousalHigh * 0.18f);
        SetTarget(FaceExpression.BrowOuterUpLeft, gate * arousalHigh * 0.18f);
        SetTarget(FaceExpression.EyeWideRight, gate * arousalHigh * 0.14f);
        SetTarget(FaceExpression.EyeWideLeft, gate * arousalHigh * 0.14f);

        Smooth(SubtleShapes, dtSeconds, AttackSeconds, DecaySeconds, offsets);

        float smile = UpdateSmileEpisode(prosody, dtSeconds) * Math.Clamp(smileIntensity, 0f, 1f);
        LastSmileEnvelope = smile;
        if (smile <= 0f)
        {
            return;
        }

        // Corner slant tracks corner pull one-to-one and dimples follow at a fixed ratio, mirroring
        // how hardware trackers report smiles; cheek and eye squint are the Duchenne pairing.
        offsets[(int)FaceExpression.MouthCornerPullRight] = smile;
        offsets[(int)FaceExpression.MouthCornerPullLeft] = smile;
        offsets[(int)FaceExpression.MouthCornerSlantRight] = smile;
        offsets[(int)FaceExpression.MouthCornerSlantLeft] = smile;
        offsets[(int)FaceExpression.MouthDimpleRight] = smile * DimpleRatio;
        offsets[(int)FaceExpression.MouthDimpleLeft] = smile * DimpleRatio;
        offsets[(int)FaceExpression.CheekSquintRight] = smile * CheekSmileRatio;
        offsets[(int)FaceExpression.CheekSquintLeft] = smile * CheekSmileRatio;
        offsets[(int)FaceExpression.EyeSquintRight] = smile * EyeSquintSmileRatio;
        offsets[(int)FaceExpression.EyeSquintLeft] = smile * EyeSquintSmileRatio;
    }

    public void Reset()
    {
        Array.Clear(_smoothed);
        Array.Clear(_target);
        _secondsToNextEpisode = -1f;
        _secondsIntoEpisode = -1f;
        _episodeDuration = 0f;
        _episodePeak = 0f;
        LastSmileEnvelope = 0f;
    }

    private float UpdateSmileEpisode(ProsodyState prosody, float dtSeconds)
    {
        if (_secondsIntoEpisode >= 0f)
        {
            _secondsIntoEpisode += dtSeconds;
            if (_secondsIntoEpisode >= _episodeDuration)
            {
                _secondsIntoEpisode = -1f;
                ScheduleNextEpisode();
                return 0f;
            }

            return Envelope(_secondsIntoEpisode, _episodeDuration) * _episodePeak;
        }

        if (_secondsToNextEpisode < 0f)
        {
            ScheduleNextEpisode();
        }

        // Smiles co-occur positively with speech (0.380 mean speaking vs 0.030 quiet), so the clock
        // only advances while talking, faster when animated.
        if (!prosody.SpeechActive)
        {
            return 0f;
        }

        _secondsToNextEpisode -= dtSeconds * (0.3f + (1.4f * Math.Clamp(prosody.Arousal, 0f, 1f)));
        if (_secondsToNextEpisode > 0f)
        {
            return 0f;
        }

        _secondsIntoEpisode = 0f;
        _episodeDuration = Lerp(DurationMinSeconds, DurationMaxSeconds, (float)_rng.NextDouble());
        _episodePeak = Lerp(PeakMin, PeakMax, (float)_rng.NextDouble());
        return 0f;
    }

    private void ScheduleNextEpisode()
    {
        float meanGapSeconds = 60f / EpisodesPerMinute;
        double u = Math.Max(1e-6, _rng.NextDouble());
        _secondsToNextEpisode = (float)(-Math.Log(u) * meanGapSeconds);
    }

    private static float Envelope(float t, float duration)
    {
        float offsetStart = Math.Max(OnsetSeconds, duration - OffsetSeconds);
        if (t < OnsetSeconds)
        {
            return SmoothStep01(t / OnsetSeconds);
        }

        if (t < offsetStart)
        {
            return 1f;
        }

        float fall = (t - offsetStart) / Math.Max(0.001f, duration - offsetStart);
        return 1f - SmoothStep01(fall);
    }

    private void Smooth(
        FaceExpression[] shapes, float dtSeconds, float attackSeconds, float decaySeconds, float[] offsets)
    {
        float attack = Coefficient(dtSeconds, attackSeconds);
        float decay = Coefficient(dtSeconds, decaySeconds);
        foreach (FaceExpression shape in shapes)
        {
            int i = (int)shape;
            float target = _target[i];
            float coeff = target > _smoothed[i] ? attack : decay;
            _smoothed[i] += (target - _smoothed[i]) * coeff;
            offsets[i] = _smoothed[i];
        }
    }

    private void SetTarget(FaceExpression shape, float value)
    {
        _target[(int)shape] = Math.Clamp(value, 0f, 1f);
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static float Coefficient(float dtSeconds, float tauSeconds)
    {
        return dtSeconds <= 0f || tauSeconds <= 0f ? 1f : 1f - MathF.Exp(-dtSeconds / tauSeconds);
    }

    private static float SmoothStep01(float x)
    {
        float t = Math.Clamp(x, 0f, 1f);
        return t * t * (3f - (2f * t));
    }
}
