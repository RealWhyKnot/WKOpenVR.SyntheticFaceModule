using WKOpenVR.FaceTracking.Sdk;
using WKOpenVR.SyntheticFaceModule.Prosody;

namespace WKOpenVR.SyntheticFaceModule.Coloring;

/// <summary>
/// Maps a <see cref="ProsodyState"/> to low-amplitude, additive expression coloring on brows,
/// cheeks, and eye squint/wide. Deliberately subtle: hard per-shape caps, confidence
/// gating, neutral bias, and fast-attack/slow-decay smoothing so a wrong guess never looks uncanny.
/// It never writes mouth shapes; the mouth solver owns every lip and jaw output.
/// </summary>
public sealed class EmotionColoringLayer
{
    private const float ConfidenceGate = 0.25f;
    private const float AttackSeconds = 0.2f;
    private const float DecaySeconds = 1.5f;

    private static readonly FaceExpression[] ColoredShapes =
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

    private readonly float[] _smoothed = new float[FaceExpressionCount.Value];
    private readonly float[] _target = new float[FaceExpressionCount.Value];

    /// <summary>Clears <paramref name="offsets"/> and writes the smoothed additive coloring into it.</summary>
    public void Apply(ProsodyState prosody, float intensity, float dtSeconds, float[] offsets)
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

        SetTarget(FaceExpression.CheekSquintRight, gate * positive * 0.18f);
        SetTarget(FaceExpression.CheekSquintLeft, gate * positive * 0.18f);
        SetTarget(FaceExpression.EyeSquintRight, gate * positive * 0.12f);
        SetTarget(FaceExpression.EyeSquintLeft, gate * positive * 0.12f);

        SetTarget(FaceExpression.BrowInnerUpRight, gate * negative * 0.18f);
        SetTarget(FaceExpression.BrowInnerUpLeft, gate * negative * 0.18f);

        SetTarget(FaceExpression.BrowOuterUpRight, gate * arousalHigh * 0.18f * (v >= 0f ? 1f : 0.4f));
        SetTarget(FaceExpression.BrowOuterUpLeft, gate * arousalHigh * 0.18f * (v >= 0f ? 1f : 0.4f));
        SetTarget(FaceExpression.EyeWideRight, gate * arousalHigh * 0.14f * (v >= 0f ? 1f : 0.6f));
        SetTarget(FaceExpression.EyeWideLeft, gate * arousalHigh * 0.14f * (v >= 0f ? 1f : 0.6f));

        SetTarget(FaceExpression.BrowLowererRight, gate * arousalHigh * negative * 0.18f);
        SetTarget(FaceExpression.BrowLowererLeft, gate * arousalHigh * negative * 0.18f);

        float attack = Coefficient(dtSeconds, AttackSeconds);
        float decay = Coefficient(dtSeconds, DecaySeconds);

        foreach (FaceExpression shape in ColoredShapes)
        {
            int i = (int)shape;
            float target = _target[i];
            float coeff = target > _smoothed[i] ? attack : decay;
            _smoothed[i] += (target - _smoothed[i]) * coeff;
            offsets[i] = _smoothed[i];
        }
    }

    public void Reset()
    {
        Array.Clear(_smoothed);
        Array.Clear(_target);
    }

    private void SetTarget(FaceExpression shape, float value)
    {
        _target[(int)shape] = Math.Clamp(value, 0f, 1f);
    }

    private static float Coefficient(float dtSeconds, float tauSeconds)
    {
        return dtSeconds <= 0f || tauSeconds <= 0f ? 1f : 1f - MathF.Exp(-dtSeconds / tauSeconds);
    }
}
