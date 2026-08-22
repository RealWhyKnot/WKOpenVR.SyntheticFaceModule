namespace WKOpenVR.SyntheticFaceModule.Config;

/// <summary>
/// User-tunable settings for the synthetic face source. Plain data; serialized to/from
/// <c>synthetic_face.json</c>. All fields have safe defaults so the module behaves well with
/// no config file present (mouth on, tone-driven expression on, eyes on, lite tier).
/// </summary>
public sealed class SyntheticConfig
{
    /// <summary>Drive mouth shapes from the microphone. Default on.</summary>
    public bool DriveMouth { get; set; } = true;

    /// <summary>Fire expression episodes from vocal tone. Default on.</summary>
    public bool DriveEmotion { get; set; } = true;

    /// <summary>
    /// Write procedural eye data (blink + gaze) timed to tracked-eye recordings. Default on; off
    /// leaves VRChat's own idle blink/auto-gaze running.
    /// </summary>
    public bool DriveEyes { get; set; } = true;

    /// <summary>
    /// Enable the intensive quality tier (ONNX speech-emotion model). Default off; the heuristic
    /// estimator is always the baseline and graceful fallback.
    /// </summary>
    public bool QualityMode { get; set; }

    /// <summary>Master gain over every tone-driven episode. 0 disables, 1 = tracked amplitudes.</summary>
    public float EmotionIntensity { get; set; } = 1.0f;

    // Per-channel switch and gain; gains multiply EmotionIntensity.
    public bool QuestionEnabled { get; set; } = true;

    public float QuestionGain { get; set; } = 1.0f;

    public bool EmphasisEnabled { get; set; } = true;

    public float EmphasisGain { get; set; } = 1.0f;

    public bool EngagementEnabled { get; set; } = true;

    public float EngagementGain { get; set; } = 1.0f;

    public bool HesitationEnabled { get; set; } = true;

    public float HesitationGain { get; set; } = 1.0f;

    public bool LaughterEnabled { get; set; } = true;

    public float LaughterGain { get; set; } = 1.0f;

    /// <summary>Scales the mouth output. 1 = nominal.</summary>
    public float MouthIntensity { get; set; } = 0.6f;

    /// <summary>
    /// Scales the always-on idle micro-motion (small brow/squint events during quiet).
    /// 0 disables it, 1 = amplitudes and rates measured from real recordings.
    /// </summary>
    public float IdleIntensity { get; set; } = 1.0f;

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
