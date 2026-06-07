using WKOpenVR.SyntheticFaceModule.Audio;

namespace WKOpenVR.SyntheticFaceModule.Prosody;

/// <summary>Compact prosodic descriptors for one window.</summary>
public readonly record struct ProsodyFeatures(
    float Loudness,
    float LogPitch,
    bool Voiced,
    float Brightness,
    float Onset,
    float ClipLevel);

/// <summary>
/// Reimplements a small GeMAPS-style feature set (loudness, F0, spectral brightness, onset/flux)
/// from the per-window audio features. These are standard, non-copyrightable feature *definitions*;
/// this is not openSMILE and embeds none of it.
/// </summary>
public static class GemapsLiteFeatures
{
    public static ProsodyFeatures Extract(AudioAnalysisFrame frame)
    {
        float loudness = 20f * MathF.Log10(frame.Rms + 1e-5f);
        float logPitch = frame.Voiced && frame.PitchHz > 0f ? MathF.Log(frame.PitchHz) : 0f;
        float brightness = Math.Clamp((frame.SpectralCentroidHz - 200f) / 3800f, 0f, 1f);
        float onset = frame.SpectralFlux;
        return new ProsodyFeatures(loudness, logPitch, frame.Voiced, brightness, onset, frame.Rms);
    }
}
