using WKOpenVR.FaceTracking.Sdk;
using WKOpenVR.SyntheticFaceModule.Audio;
using WKOpenVR.SyntheticFaceModule.Dsp;
using WKOpenVR.SyntheticFaceModule.Prosody;

namespace WKOpenVR.SyntheticFaceModule.Mouth;

// Two-stage mouth: a dt-aware jaw envelope (driven by VAD activity, fast attack / slow release)
// gates a blended broad-viseme classifier. The jaw uses the blended group weights; the lips use a
// single winning posture, because rounding and spreading are mutually exclusive in real tracker
// output (0 co-active frames in 77851 sampled Virtual Desktop frames). Writes only mouth/jaw
// indices into the supplied 88-length expression buffer; everything else is zeroed so the mixer can
// layer emotion on top.
public sealed class MouthSolver
{
    private const float GroupSmoothingSeconds = 0.04f;
    private const float MouthCloseCap = 0.25f;

    // A posture must actually dominate before it drives the lips. Real trackers hold funnel/pucker
    // above 0.10 in well under 1% of frames, so an ungated blend is wrong by orders of magnitude.
    private const float PostureFloor = 0.12f;
    private const float PostureMargin = 1.25f;
    private const float PostureMinDwellSeconds = 0.04f;

    // Bilabial closure shows up as an energy dip inside an utterance, not at its onset.
    private const float ClosureContextSeconds = 0.40f;
    private const float ClosureContextMin = 0.35f;
    private const float ClosureDipMin = 0.35f;

    // Prior spread of the 0..1 centroid scale (~380 Hz) until the speaker's own window fills.
    private const float CentroidPriorVariance = 0.01f;

    private readonly AsymmetricSmoother _jaw = new(attackSeconds: 0.02f, releaseSeconds: 0.09f);
    private readonly AsymmetricSmoother _mouthClosed = new(attackSeconds: 0.025f, releaseSeconds: 0.12f);
    private readonly BroadVisemeClassifier _classifier = new();
    private readonly RunningBaseline _centroid = new(20f, CentroidPriorVariance);

    private float _open;
    private float _front;
    private float _rounded;
    private float _fricative;
    private LipPosture _posture;
    private float _postureHeldSeconds;
    private float _speechContext;

    private enum LipPosture
    {
        None,
        Rounded,
        Front,
    }

    public float LastJawOpen { get; private set; }

    public float LastMouthClosed { get; private set; }

    public float LastOpenWeight => _open;

    public float LastFrontWeight => _front;

    public float LastRoundedWeight => _rounded;

    public float LastFricativeWeight => _fricative;

    // Fills `expressions` (length 88) with mouth shapes for this frame. `activity` is the VAD speech
    // strength 0..1; `intensity` scales the output (config MouthIntensity).
    public void Solve(AudioAnalysisFrame frame, float activity, float dtSeconds, float intensity, float[] expressions)
    {
        Array.Clear(expressions);

        activity = Math.Clamp(activity, 0f, 1f);
        float jaw = _jaw.Update(activity, dtSeconds);

        float centroidZ = 0f;
        if (frame.Voiced && activity > 0f)
        {
            float norm = BroadVisemeClassifier.CentroidNorm(frame.SpectralCentroidHz);
            centroidZ = Math.Clamp(_centroid.Update(norm, dtSeconds), -4f, 4f);
        }

        VisemeWeights groups = _classifier.Classify(frame, activity, centroidZ);
        float k = Coefficient(dtSeconds, GroupSmoothingSeconds);
        _open += (groups.Open - _open) * k;
        _front += (groups.Front - _front) * k;
        _rounded += (groups.Rounded - _rounded) * k;
        _fricative += (groups.Fricative - _fricative) * k;

        UpdatePosture(dtSeconds);

        float openFactor = Math.Clamp(
            0.55f + (0.45f * _open) - (0.25f * _rounded) - (0.35f * _front) - (0.40f * _fricative),
            0.10f,
            1.0f);

        float jawOpen = jaw * openFactor;

        _speechContext += (activity - _speechContext) * Coefficient(dtSeconds, ClosureContextSeconds);
        float dip = _speechContext >= ClosureContextMin ? Math.Clamp(_speechContext - activity, 0f, 1f) : 0f;
        float mouthClosed = _mouthClosed.Update(dip >= ClosureDipMin ? dip * MouthCloseCap : 0f, dtSeconds);

        float rounding = _posture == LipPosture.Rounded ? _rounded : 0f;
        float spreading = _posture == LipPosture.Front ? _front : 0f;

        float funnel = jaw * rounding * 0.60f;
        float pucker = jaw * rounding * 0.45f;
        float stretch = jaw * spreading * 0.55f;
        // Lip-opener ratios over speaking frames, pooled from 15 tracker recordings.
        float upperUp = jawOpen * 0.82f;
        float lowerDown = jawOpen * 0.83f;

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

        // Hardware trackers report UpperDeepen locked to UpperUp; mirror the pairing.
        Set(expressions, FaceExpression.MouthUpperUpRight, upperUp, intensity);
        Set(expressions, FaceExpression.MouthUpperUpLeft, upperUp, intensity);
        Set(expressions, FaceExpression.MouthUpperDeepenRight, upperUp, intensity);
        Set(expressions, FaceExpression.MouthUpperDeepenLeft, upperUp, intensity);

        Set(expressions, FaceExpression.MouthLowerDownRight, lowerDown, intensity);
        Set(expressions, FaceExpression.MouthLowerDownLeft, lowerDown, intensity);

        LastJawOpen = expressions[(int)FaceExpression.JawOpen];
        LastMouthClosed = expressions[(int)FaceExpression.MouthClosed];
    }

    public void Reset()
    {
        _jaw.Reset();
        _mouthClosed.Reset();
        _centroid.Reset();
        _open = 0f;
        _front = 0f;
        _rounded = 0f;
        _fricative = 0f;
        _posture = LipPosture.None;
        _postureHeldSeconds = 0f;
        _speechContext = 0f;
        LastJawOpen = 0f;
        LastMouthClosed = 0f;
    }

    private void UpdatePosture(float dtSeconds)
    {
        _postureHeldSeconds += dtSeconds;

        LipPosture challenger = _rounded >= _front ? LipPosture.Rounded : LipPosture.Front;
        float challengerWeight = Math.Max(_rounded, _front);

        if (challengerWeight < PostureFloor)
        {
            if (_posture != LipPosture.None)
            {
                _posture = LipPosture.None;
                _postureHeldSeconds = 0f;
            }

            return;
        }

        if (challenger == _posture)
        {
            return;
        }

        float incumbentWeight = _posture == LipPosture.Rounded ? _rounded : _front;
        if (_posture != LipPosture.None &&
            (_postureHeldSeconds < PostureMinDwellSeconds || challengerWeight < incumbentWeight * PostureMargin))
        {
            return;
        }

        _posture = challenger;
        _postureHeldSeconds = 0f;
    }

    private static void Set(float[] expressions, FaceExpression expression, float value, float intensity)
    {
        expressions[(int)expression] = Math.Clamp(value * intensity, 0f, 1f);
    }

    private static float Coefficient(float dtSeconds, float tauSeconds)
    {
        return dtSeconds <= 0f ? 1f : 1f - MathF.Exp(-dtSeconds / tauSeconds);
    }
}
