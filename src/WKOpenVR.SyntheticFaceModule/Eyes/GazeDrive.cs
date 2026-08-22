namespace WKOpenVR.SyntheticFaceModule.Eyes;

// All-zero default reproduces audio-only gaze: no head coupling, no social split.
public readonly record struct GazeDrive(
    float HeadYawRate,
    float HeadPitchRate,
    bool HeadMoving,
    bool Speaking,
    bool SocialGaze,
    float VorGain,
    float VorRecenterSeconds);
