using WKOpenVR.FaceTracking.Sdk;
using WKOpenVR.SyntheticFaceModule.Audio;
using WKOpenVR.SyntheticFaceModule.Dsp;

namespace WKOpenVR.SyntheticFaceModule.Mouth;

/// <summary>
/// Two-stage mouth: a dt-aware jaw envelope (driven by VAD activity, fast attack / slow release)
/// gates a blended broad-viseme classifier whose smoothed group weights (coarticulation) shape the
/// lips. Writes only mouth/jaw indices into the supplied 88-length expression buffer; everything
/// else is zeroed so the mixer can layer emotion on top. Pure and deterministic.
/// </summary>
public sealed class MouthSolver
{
    private const float GroupSmoothingSeconds = 0.04f;

    private readonly AsymmetricSmoother _jaw = new(attackSeconds: 0.02f, releaseSeconds: 0.09f);
    private readonly BroadVisemeClassifier _classifier = new();

    private float _open;
    private float _front;
    private float _rounded;
    private float _fricative;

    /// <summary>
    /// Fills <paramref name="expressions"/> (length 88) with mouth shapes for this frame.
    /// <paramref name="activity"/> is the VAD speech strength 0..1; <paramref name="intensity"/>
    /// scales the output (config MouthIntensity).
    /// </summary>
    public void Solve(AudioAnalysisFrame frame, float activity, float dtSeconds, float intensity, float[] expressions)
    {
        Array.Clear(expressions);

        float jaw = _jaw.Update(Math.Clamp(activity, 0f, 1f), dtSeconds);

        VisemeWeights groups = _classifier.Classify(frame, activity);
        float k = SmoothingCoefficient(dtSeconds);
        _open += (groups.Open - _open) * k;
        _front += (groups.Front - _front) * k;
        _rounded += (groups.Rounded - _rounded) * k;
        _fricative += (groups.Fricative - _fricative) * k;

        float openFactor = Math.Clamp(
            0.55f + (0.45f * _open) - (0.25f * _rounded) - (0.35f * _front) - (0.40f * _fricative),
            0.10f,
            1.0f);

        float jawOpen = jaw * openFactor;
        float mouthClosed = Math.Clamp(1f - (jaw * 1.6f), 0f, 1f);
        float funnel = jaw * _rounded * 0.60f;
        float pucker = jaw * _rounded * 0.45f;
        float stretch = jaw * ((_front * 0.55f) + (_fricative * 0.35f));
        float tightener = jaw * _fricative * 0.40f;
        float upperUp = jawOpen * 0.20f;

        Set(expressions, FaceExpression.JawOpen, jawOpen, intensity);
        Set(expressions, FaceExpression.MouthClosed, mouthClosed, intensity);

        Set(expressions, FaceExpression.LipFunnelUpperRight, funnel, intensity);
        Set(expressions, FaceExpression.LipFunnelUpperLeft, funnel, intensity);
        Set(expressions, FaceExpression.LipFunnelLowerRight, funnel, intensity);
        Set(expressions, FaceExpression.LipFunnelLowerLeft, funnel, intensity);

        Set(expressions, FaceExpression.LipPuckerUpperRight, pucker, intensity);
        Set(expressions, FaceExpression.LipPuckerUpperLeft, pucker, intensity);
        Set(expressions, FaceExpression.LipPuckerLowerRight, pucker, intensity);
        Set(expressions, FaceExpression.LipPuckerLowerLeft, pucker, intensity);

        Set(expressions, FaceExpression.MouthStretchRight, stretch, intensity);
        Set(expressions, FaceExpression.MouthStretchLeft, stretch, intensity);

        Set(expressions, FaceExpression.MouthTightenerRight, tightener, intensity);
        Set(expressions, FaceExpression.MouthTightenerLeft, tightener, intensity);

        Set(expressions, FaceExpression.MouthUpperUpRight, upperUp, intensity);
        Set(expressions, FaceExpression.MouthUpperUpLeft, upperUp, intensity);
    }

    public void Reset()
    {
        _jaw.Reset();
        _open = 0f;
        _front = 0f;
        _rounded = 0f;
        _fricative = 0f;
    }

    private static void Set(float[] expressions, FaceExpression expression, float value, float intensity)
    {
        expressions[(int)expression] = Math.Clamp(value * intensity, 0f, 1f);
    }

    private static float SmoothingCoefficient(float dtSeconds)
    {
        return dtSeconds <= 0f ? 1f : 1f - MathF.Exp(-dtSeconds / GroupSmoothingSeconds);
    }
}
