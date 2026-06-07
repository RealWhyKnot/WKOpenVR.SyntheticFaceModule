using WKOpenVR.SyntheticFaceModule.Audio;

namespace WKOpenVR.SyntheticFaceModule.Prosody;

/// <summary>Continuous, dimensional emotion estimate (not categorical labels).</summary>
public readonly record struct ProsodyState(float Arousal, float Valence, float Confidence, bool SpeechActive)
{
    public static ProsodyState Neutral => new(0f, 0f, 0f, false);
}

/// <summary>
/// Estimates dimensional prosody (arousal 0..1, valence -1..1, confidence 0..1) from audio. The
/// heuristic implementation is always available; the quality tier provides a drop-in ONNX-backed
/// implementation behind the same interface so the coloring layer is unchanged.
/// </summary>
public interface IProsodyEstimator
{
    ProsodyState Estimate(AudioAnalysisFrame frame, float activity, bool isSpeech, float dtSeconds);

    void Reset();
}
