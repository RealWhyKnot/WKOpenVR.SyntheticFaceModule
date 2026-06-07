namespace WKOpenVR.SyntheticFaceModule.Config;

/// <summary>
/// User-tunable settings for the synthetic face source. Plain data; serialized to/from
/// <c>synthetic_face.json</c>. All fields have safe defaults so the module behaves well with
/// no config file present (mouth on, emotion subtle, eyes off, lite tier).
/// </summary>
public sealed class SyntheticConfig
{
    /// <summary>Drive mouth shapes from the microphone. Default on.</summary>
    public bool DriveMouth { get; set; } = true;

    /// <summary>Apply subtle prosody-driven expression coloring. Default on but low intensity.</summary>
    public bool DriveEmotion { get; set; } = true;

    /// <summary>
    /// Write procedural eye data (blink + gaze). Default OFF so VRChat's free idle blink/auto-gaze
    /// runs; turning this on overrides VRChat's native eyes with our procedural ones.
    /// </summary>
    public bool DriveEyes { get; set; }

    /// <summary>
    /// Enable the intensive quality tier (ONNX speech-emotion model). Default off; the heuristic
    /// estimator is always the baseline and graceful fallback.
    /// </summary>
    public bool QualityMode { get; set; }

    /// <summary>Scales the emotion coloring caps. 0 disables, 1 = full design caps.</summary>
    public float EmotionIntensity { get; set; } = 1.0f;

    /// <summary>Scales the mouth output. 1 = nominal.</summary>
    public float MouthIntensity { get; set; } = 1.0f;

    /// <summary>WaveIn device index; -1 selects the default capture device (WAVE_MAPPER).</summary>
    public int MicDeviceNumber { get; set; } = -1;

    /// <summary>Optional friendly device name to prefer when present (matched case-insensitively).</summary>
    public string? MicDeviceName { get; set; }

    /// <summary>
    /// Optional path to a license-clean ONNX speech-emotion model. When absent, the quality tier
    /// falls back to the heuristic estimator (no weights are bundled).
    /// </summary>
    public string? EmotionModelPath { get; set; }

    public SyntheticConfig Clone() => (SyntheticConfig)MemberwiseClone();
}
