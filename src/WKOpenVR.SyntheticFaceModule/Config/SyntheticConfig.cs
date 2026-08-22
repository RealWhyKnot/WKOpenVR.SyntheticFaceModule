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
    public float MouthIntensity { get; set; } = 0.4f;

    /// <summary>
    /// Spontaneous blink rate. The default is the pooled rate measured from tracked sessions;
    /// gaze shifts and arousal still modulate it around this figure.
    /// </summary>
    public float BlinkRatePerMinute { get; set; } = 15.9f;

    /// <summary>
    /// Scales the always-on idle micro-motion (small brow/squint events during quiet).
    /// 0 disables it, 1 = amplitudes and rates measured from real recordings.
    /// </summary>
    public float IdleIntensity { get; set; } = 1.0f;

    // Eye counter-rotation against head movement. 0 disables head coupling entirely.
    public float VorGain { get; set; } = 0.95f;

    // How long the eyes take to drift back to centre once the head stops, seconds.
    public float VorRecenterSeconds { get; set; } = 0.3f;

    // Head speed above which idle saccades pause, rad/s.
    public float HeadMovingThreshold { get; set; } = 0.5f;

    // Look at the listener more while silent than while speaking.
    public bool SocialGazeEnabled { get; set; } = true;

    // Left/right expression asymmetry. 0 restores a perfectly even face.
    public float AsymmetryIntensity { get; set; } = 1.0f;

    // Let the eyes close when the head has been down, still and silent for a long time.
    public bool DozeEnabled { get; set; } = true;

    // Seconds all three doze gates must hold before the lids start to fall.
    public float DozeDwellSeconds { get; set; } = 45.0f;

    // Further seconds of dozing before the eyes go to nearly shut.
    public float SleepDwellSeconds { get; set; } = 60.0f;

    // Averaged head speed below which the head counts as still, radians per second. Wearing a
    // headset keeps the head moving, so genuine stillness is the whole signal.
    public float DozeStillnessRadPerSecond { get; set; } = 0.06f;

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
