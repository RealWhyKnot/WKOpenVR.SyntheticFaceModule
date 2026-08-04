using WKOpenVR.FaceTracking.Sdk;
using WKOpenVR.SyntheticFaceModule.Prosody;

namespace WKOpenVR.SyntheticFaceModule.Coloring;

/// <summary>
/// Maps a <see cref="ProsodyState"/> to additive expression offsets. Two tiers: subtle
/// brow/cheek/eye coloring with hard low caps, and a full-range smile/frown channel on the
/// mouth corners tuned to real face-tracking recordings (smiles reach 1.0 with a ~0.3 s
/// rise and a slow multi-second release; frowns are brief low microexpressions). Positive
/// valence also raises cheek and eye squint with the smile, matching how real smiles
/// engage the whole face. Everything is confidence-gated and clamped, and the layer never
/// writes viseme-critical shapes: jaw, MouthClosed, funnel, pucker, stretch, tightener,
/// and the upper/lower lip openers stay owned by the audio mouth solver.
/// </summary>
public sealed class EmotionColoringLayer
{
    private const float ConfidenceGate = 0.25f;
    private const float AttackSeconds = 0.2f;
    private const float DecaySeconds = 1.5f;

    private const float SmileCap = 1.0f;
    private const float SmileAttackSeconds = 0.30f;
    private const float SmileDecaySeconds = 1.8f;
    private const float SmileOnsetValence = 0.05f;
    private const float SmileFullValence = 0.45f;
    private const float DimpleRatio = 0.37f;
    private const float CheekSmileRatio = 0.55f;
    private const float EyeSquintSmileRatio = 0.35f;

    private const float FrownCap = 0.5f;
    private const float FrownAttackSeconds = 0.06f;
    private const float FrownDecaySeconds = 0.35f;
    private const float FrownOnsetValence = 0.15f;
    private const float FrownFullValence = 0.55f;

    private static readonly FaceExpression[] SubtleShapes =
    {
        FaceExpression.CheekSquintRight,
        FaceExpression.CheekSquintLeft,
        FaceExpression.EyeSquintRight,
        FaceExpression.EyeSquintLeft,
        FaceExpression.BrowInnerUpRight,
        FaceExpression.BrowInnerUpLeft,
        FaceExpression.BrowOuterUpRight,
        FaceExpression.BrowOuterUpLeft,
        FaceExpression.EyeWideRight,
        FaceExpression.EyeWideLeft,
        FaceExpression.BrowLowererRight,
        FaceExpression.BrowLowererLeft,
    };

    private static readonly FaceExpression[] SmileShapes =
    {
        FaceExpression.MouthCornerPullRight,
        FaceExpression.MouthCornerPullLeft,
        FaceExpression.MouthCornerSlantRight,
        FaceExpression.MouthCornerSlantLeft,
        FaceExpression.MouthDimpleRight,
        FaceExpression.MouthDimpleLeft,
    };

    private static readonly FaceExpression[] FrownShapes =
    {
        FaceExpression.MouthFrownRight,
        FaceExpression.MouthFrownLeft,
    };

    private readonly float[] _smoothed = new float[FaceExpressionCount.Value];
    private readonly float[] _target = new float[FaceExpressionCount.Value];

    /// <summary>Clears <paramref name="offsets"/> and writes the smoothed additive coloring into it.</summary>
    public void Apply(ProsodyState prosody, float intensity, float smileIntensity, float dtSeconds, float[] offsets)
    {
        Array.Clear(offsets);
        Array.Clear(_target);

        float gate = prosody.SpeechActive && prosody.Confidence >= ConfidenceGate
            ? prosody.Confidence * Math.Clamp(intensity, 0f, 1f)
            : 0f;

        float v = prosody.Valence;
        float a = prosody.Arousal;
        float positive = Math.Clamp(v, 0f, 1f);
        float negative = Math.Clamp(-v, 0f, 1f);
        float arousalHigh = Math.Clamp((a - 0.5f) * 2f, 0f, 1f);

        // The heuristic estimator caps valence well below 1, so shape the smile drive to
        // reach full amplitude at a moderately positive valence; real smiles saturate.
        float smile = Math.Clamp(smileIntensity, 0f, 1f);
        float smileDrive = gate * SmoothStep(SmileOnsetValence, SmileFullValence, positive) * smile;
        float frownDrive = gate * SmoothStep(FrownOnsetValence, FrownFullValence, negative) * smile;

        SetTarget(FaceExpression.CheekSquintRight, Math.Max(gate * positive * 0.18f, smileDrive * CheekSmileRatio));
        SetTarget(FaceExpression.CheekSquintLeft, Math.Max(gate * positive * 0.18f, smileDrive * CheekSmileRatio));
        SetTarget(FaceExpression.EyeSquintRight, Math.Max(gate * positive * 0.12f, smileDrive * EyeSquintSmileRatio));
        SetTarget(FaceExpression.EyeSquintLeft, Math.Max(gate * positive * 0.12f, smileDrive * EyeSquintSmileRatio));

        SetTarget(FaceExpression.BrowInnerUpRight, gate * negative * 0.18f);
        SetTarget(FaceExpression.BrowInnerUpLeft, gate * negative * 0.18f);

        SetTarget(FaceExpression.BrowOuterUpRight, gate * arousalHigh * 0.18f * (v >= 0f ? 1f : 0.4f));
        SetTarget(FaceExpression.BrowOuterUpLeft, gate * arousalHigh * 0.18f * (v >= 0f ? 1f : 0.4f));
        SetTarget(FaceExpression.EyeWideRight, gate * arousalHigh * 0.14f * (v >= 0f ? 1f : 0.6f));
        SetTarget(FaceExpression.EyeWideLeft, gate * arousalHigh * 0.14f * (v >= 0f ? 1f : 0.6f));

        SetTarget(FaceExpression.BrowLowererRight, gate * arousalHigh * negative * 0.18f);
        SetTarget(FaceExpression.BrowLowererLeft, gate * arousalHigh * negative * 0.18f);

        // Corner slant tracks corner pull one-to-one and dimples follow at a fixed ratio,
        // mirroring how hardware trackers report smiles.
        SetTarget(FaceExpression.MouthCornerPullRight, smileDrive * SmileCap);
        SetTarget(FaceExpression.MouthCornerPullLeft, smileDrive * SmileCap);
        SetTarget(FaceExpression.MouthCornerSlantRight, smileDrive * SmileCap);
        SetTarget(FaceExpression.MouthCornerSlantLeft, smileDrive * SmileCap);
        SetTarget(FaceExpression.MouthDimpleRight, smileDrive * DimpleRatio);
        SetTarget(FaceExpression.MouthDimpleLeft, smileDrive * DimpleRatio);

        SetTarget(FaceExpression.MouthFrownRight, frownDrive * FrownCap);
        SetTarget(FaceExpression.MouthFrownLeft, frownDrive * FrownCap);

        Smooth(SubtleShapes, dtSeconds, AttackSeconds, DecaySeconds, offsets);
        Smooth(SmileShapes, dtSeconds, SmileAttackSeconds, SmileDecaySeconds, offsets);
        Smooth(FrownShapes, dtSeconds, FrownAttackSeconds, FrownDecaySeconds, offsets);
    }

    public void Reset()
    {
        Array.Clear(_smoothed);
        Array.Clear(_target);
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

    private static float Coefficient(float dtSeconds, float tauSeconds)
    {
        return dtSeconds <= 0f || tauSeconds <= 0f ? 1f : 1f - MathF.Exp(-dtSeconds / tauSeconds);
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }
}
