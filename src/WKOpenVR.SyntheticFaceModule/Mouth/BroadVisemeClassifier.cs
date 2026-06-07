using WKOpenVR.SyntheticFaceModule.Audio;

namespace WKOpenVR.SyntheticFaceModule.Mouth;

/// <summary>Blended membership of an audio window across broad mouth-shape groups (sum across the
/// non-silence groups equals the speech activity level).</summary>
public readonly record struct VisemeWeights(float Silence, float Open, float Front, float Rounded, float Fricative)
{
    public static VisemeWeights SilenceOnly => new(1f, 0f, 0f, 0f, 0f);
}

/// <summary>
/// Classifies an audio window into a small set of robust, blended viseme groups using interpretable
/// spectral cues (centroid, zero-crossing rate, voicing) rather than attempting a brittle 15-class
/// phoneme recognition. Bilabial closures are handled by the mouth envelope, not here. Pure and
/// deterministic.
/// </summary>
public sealed class BroadVisemeClassifier
{
    public VisemeWeights Classify(AudioAnalysisFrame frame, float activity)
    {
        activity = Math.Clamp(activity, 0f, 1f);
        if (activity <= 0.001f)
        {
            return VisemeWeights.SilenceOnly;
        }

        float centroidNorm = Math.Clamp((frame.SpectralCentroidHz - 200f) / 3800f, 0f, 1f);
        float zcrNorm = Math.Clamp(frame.ZeroCrossingRate / 0.35f, 0f, 1f);
        float voiced = frame.Voiced ? 1f : 0f;
        float unvoiced = 1f - voiced;

        float fricative = unvoiced * SmoothStep(0.45f, 0.85f, zcrNorm) * SmoothStep(0.45f, 0.85f, centroidNorm);
        float front = voiced * SmoothStep(0.45f, 0.80f, centroidNorm);
        float rounded = voiced * (1f - SmoothStep(0.15f, 0.50f, centroidNorm));
        float open = voiced * Bell(centroidNorm, 0.32f, 0.18f);

        float sum = fricative + front + rounded + open;
        if (sum < 1e-4f)
        {
            // Voiced but ambiguous spectrum: default to an open vowel; otherwise unvoiced silence.
            if (voiced > 0f)
            {
                return new VisemeWeights(1f - activity, activity, 0f, 0f, 0f);
            }

            return new VisemeWeights(1f - activity, 0f, 0f, 0f, activity);
        }

        float scale = activity / sum;
        return new VisemeWeights(
            Silence: 1f - activity,
            Open: open * scale,
            Front: front * scale,
            Rounded: rounded * scale,
            Fricative: fricative * scale);
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        if (edge0 == edge1)
        {
            return x < edge0 ? 0f : 1f;
        }

        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    private static float Bell(float x, float center, float width)
    {
        float d = (x - center) / width;
        return MathF.Exp(-0.5f * d * d);
    }
}
