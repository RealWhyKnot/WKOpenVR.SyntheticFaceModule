using WKOpenVR.SyntheticFaceModule.Audio;
using WKOpenVR.SyntheticFaceModule.Prosody;

namespace WKOpenVR.SyntheticFaceModule.Ser;

/// <summary>
/// Quality-tier estimator: blends the always-on heuristic with the ONNX model behind the same
/// interface so the coloring layer is unchanged. Arousal stays mostly heuristic (already decent);
/// valence leans on the model (the heuristic's weak spot). The blend weight tracks the model's
/// availability/confidence and is smoothed so toggling or load/unload never pops the face. When the
/// model is unavailable this is exactly the heuristic.
/// </summary>
public sealed class CrossfadeProsodyEstimator : IProsodyEstimator, IDisposable
{
    private readonly IProsodyEstimator _heuristic;
    private readonly OnnxProsodyEstimator _model;
    private float _blend;

    public CrossfadeProsodyEstimator(IProsodyEstimator heuristic, OnnxProsodyEstimator model)
    {
        _heuristic = heuristic;
        _model = model;
    }

    public ProsodyState Estimate(AudioAnalysisFrame frame, float activity, bool isSpeech, float dtSeconds)
    {
        ProsodyState h = _heuristic.Estimate(frame, activity, isSpeech, dtSeconds);
        if (!_model.Available)
        {
            DecayBlend(dtSeconds);
            return h;
        }

        ProsodyState m = _model.Estimate(frame, activity, isSpeech, dtSeconds);

        float targetBlend = Math.Clamp(m.Confidence, 0f, 1f);
        float coeff = dtSeconds <= 0f ? 1f : 1f - MathF.Exp(-dtSeconds / 0.5f);
        _blend += (targetBlend - _blend) * coeff;

        float arousal = Lerp(h.Arousal, m.Arousal, _blend * 0.5f);
        float valence = Lerp(h.Valence, m.Valence, _blend);
        float confidence = MathF.Max(h.Confidence, m.Confidence * _blend);
        bool speech = h.SpeechActive || m.SpeechActive;

        return new ProsodyState(
            Math.Clamp(arousal, 0f, 1f),
            Math.Clamp(valence, -1f, 1f),
            Math.Clamp(confidence, 0f, 1f),
            speech);
    }

    public void Reset()
    {
        _heuristic.Reset();
        _model.Reset();
        _blend = 0f;
    }

    public void Dispose() => _model.Dispose();

    private void DecayBlend(float dtSeconds)
    {
        float coeff = dtSeconds <= 0f ? 1f : 1f - MathF.Exp(-dtSeconds / 0.5f);
        _blend += (0f - _blend) * coeff;
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}
