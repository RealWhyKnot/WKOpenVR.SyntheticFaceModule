using WKOpenVR.SyntheticFaceModule.Audio;

namespace WKOpenVR.SyntheticFaceModule.Prosody;

/// <summary>
/// Always-on, lightweight prosody estimator. Arousal is reasonably inferable from loudness, pitch
/// height/variability, and onset rate (z-scored against the speaker's baseline). Valence is
/// acknowledged-weak from audio alone, so it is damped and the confidence it carries is modest;
/// downstream coloring keeps it subtle. Outputs decay toward neutral during silence.
/// </summary>
public sealed class HeuristicProsodyEstimator : IProsodyEstimator
{
    private readonly RunningBaseline _loudness = new(20f);
    private readonly RunningBaseline _pitch = new(20f);
    private readonly RunningBaseline _brightness = new(20f);
    private readonly RunningBaseline _onset = new(20f);

    private float _absPitchZ;
    private float _arousal;
    private float _valence;
    private float _confidence;

    public ProsodyState Estimate(AudioAnalysisFrame frame, float activity, bool isSpeech, float dtSeconds)
    {
        if (!isSpeech)
        {
            float decay = dtSeconds <= 0f ? 1f : 1f - MathF.Exp(-dtSeconds / 0.8f);
            _arousal += (0f - _arousal) * decay;
            _valence += (0f - _valence) * decay;
            _confidence += (0f - _confidence) * decay;
            return new ProsodyState(Clamp01(_arousal), Math.Clamp(_valence, -1f, 1f), Clamp01(_confidence), false);
        }

        ProsodyFeatures f = GemapsLiteFeatures.Extract(frame);
        float zLoud = _loudness.Update(f.Loudness, dtSeconds);
        float zPitch = f.Voiced ? _pitch.Update(f.LogPitch, dtSeconds) : 0f;
        float zBright = _brightness.Update(f.Brightness, dtSeconds);
        float zOnset = _onset.Update(f.Onset, dtSeconds);

        float absAlpha = dtSeconds <= 0f ? 1f : 1f - MathF.Exp(-dtSeconds / 2.0f);
        _absPitchZ += (MathF.Abs(zPitch) - _absPitchZ) * absAlpha;

        float arousalRaw = (0.40f * zLoud) + (0.30f * zPitch) + (0.20f * _absPitchZ) + (0.10f * zOnset);
        float arousalTarget = Logistic(arousalRaw);

        // Valence is fragile from audio: rising/bright voice leans positive; very loud + low pitch
        // leans tense/negative. Damped hard and carried with modest confidence.
        float valenceRaw = (0.5f * zPitch) + (0.3f * zBright) - (0.4f * MathF.Max(0f, zLoud - 1f));
        float valenceTarget = MathF.Tanh(0.6f * valenceRaw) * 0.6f;

        float clipFactor = f.ClipLevel > 0.95f ? 0.3f : 1f;
        float voicedFactor = f.Voiced ? 1f : 0.5f;
        float confidenceTarget = Clamp01(activity * voicedFactor * clipFactor);

        float k = dtSeconds <= 0f ? 1f : 1f - MathF.Exp(-dtSeconds / 0.25f);
        _arousal += (arousalTarget - _arousal) * k;
        _valence += (valenceTarget - _valence) * k;
        _confidence += (confidenceTarget - _confidence) * k;

        return new ProsodyState(Clamp01(_arousal), Math.Clamp(_valence, -1f, 1f), Clamp01(_confidence), true);
    }

    public void Reset()
    {
        _loudness.Reset();
        _pitch.Reset();
        _brightness.Reset();
        _onset.Reset();
        _absPitchZ = 0f;
        _arousal = 0f;
        _valence = 0f;
        _confidence = 0f;
    }

    private static float Logistic(float x) => 1f / (1f + MathF.Exp(-x));

    private static float Clamp01(float x) => Math.Clamp(x, 0f, 1f);
}
