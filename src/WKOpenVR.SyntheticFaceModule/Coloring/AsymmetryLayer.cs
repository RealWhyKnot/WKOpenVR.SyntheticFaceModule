using WKOpenVR.FaceTracking.Sdk;

namespace WKOpenVR.SyntheticFaceModule.Coloring;

// Real expressions are not mirror-symmetric: one side leads by a few tens of milliseconds and runs
// slightly stronger, and a perfectly even face reads as mechanical. This runs after the mixer, so
// every layer below it keeps its own symmetric contract and only the delivered frame is uneven.
// One draw per episode is shared by all pairs: independent per-pair draws would look palsied.
// Every shape an episode co-activates is listed, even ones a given avatar may not bind, because
// holding back a smile's corner pull without its dimple and cheek squint would break the fixed
// ratios that make the expression read as one movement. The trailing side is a first-order lag of
// the leading one, which cannot move faster than its source and so cannot breach a slew ceiling,
// and which scales linearly and so leaves those within-side ratios exact.
public sealed class AsymmetryLayer
{
    private const float ActiveThreshold = 0.02f;
    private const float DeltaMin = 0.05f;
    private const float DeltaMax = 0.15f;
    private const float LagMinSeconds = 0.025f;
    private const float LagMaxSeconds = 0.055f;

    // Expressions starting on the left are judged more authentic, so the coin is weighted.
    private const double LeftLeadsProbability = 0.6;

    private const float OpennessDeltaScale = 0.2f;

    private static readonly int[] RightIndices =
    [
        (int)FaceExpression.EyeSquintRight,
        (int)FaceExpression.EyeWideRight,
        (int)FaceExpression.BrowPinchRight,
        (int)FaceExpression.BrowLowererRight,
        (int)FaceExpression.BrowInnerUpRight,
        (int)FaceExpression.BrowOuterUpRight,
        (int)FaceExpression.CheekSquintRight,
        (int)FaceExpression.MouthCornerPullRight,
        (int)FaceExpression.MouthCornerSlantRight,
        (int)FaceExpression.MouthDimpleRight,
    ];

    private readonly Random _rng;
    private readonly float[] _trailing = new float[RightIndices.Length];
    private bool _wasActive;
    private float _delta;
    private float _lagSeconds;
    private bool _leftLeads;

    public AsymmetryLayer(Random rng)
    {
        _rng = rng;
        Redraw();
    }

    public float Delta => _delta;

    public float LagSeconds => _lagSeconds;

    public bool LeftLeads => _leftLeads;

    public void Apply(FaceFrame frame, float dtSeconds, float intensity)
    {
        float scale = Math.Clamp(intensity, 0f, 2f);
        if (scale <= 0f)
        {
            _wasActive = false;
            Array.Clear(_trailing);
            return;
        }

        float[] expressions = frame.Expressions;
        bool active = false;
        for (int i = 0; i < RightIndices.Length; i++)
        {
            int right = RightIndices[i];
            if (expressions[right] > ActiveThreshold || expressions[right + 1] > ActiveThreshold)
            {
                active = true;
                break;
            }
        }

        // Only redraw between expressions, so the sides never swap mid-movement.
        if (active && !_wasActive)
        {
            Redraw();
        }

        _wasActive = active;

        float delta = Math.Clamp(_delta * scale, 0f, 0.9f);
        float alpha = dtSeconds > 0f
            ? 1f - MathF.Exp(-dtSeconds / MathF.Max(1e-3f, _lagSeconds * scale))
            : 1f;

        for (int i = 0; i < RightIndices.Length; i++)
        {
            int right = RightIndices[i];
            int left = right + 1;

            // The mixer writes both sides equally; either one is the symmetric source.
            float value = MathF.Max(expressions[right], expressions[left]);
            _trailing[i] += ((value * (1f - delta)) - _trailing[i]) * alpha;
            float trailing = Math.Clamp(_trailing[i], 0f, 1f);

            if (_leftLeads)
            {
                expressions[left] = value;
                expressions[right] = trailing;
            }
            else
            {
                expressions[right] = value;
                expressions[left] = trailing;
            }
        }

        // A lid difference this small survives the driver's eyelid sync without reading as a wink.
        float opennessDelta = 1f - (delta * OpennessDeltaScale);
        if (_leftLeads)
        {
            frame.Eye.Right.Openness *= opennessDelta;
        }
        else
        {
            frame.Eye.Left.Openness *= opennessDelta;
        }
    }

    public void Reset()
    {
        Array.Clear(_trailing);
        _wasActive = false;
        Redraw();
    }

    private void Redraw()
    {
        _delta = Lerp(DeltaMin, DeltaMax, (float)_rng.NextDouble());
        _lagSeconds = Lerp(LagMinSeconds, LagMaxSeconds, (float)_rng.NextDouble());
        _leftLeads = _rng.NextDouble() < LeftLeadsProbability;
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}
