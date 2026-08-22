namespace WKOpenVR.SyntheticFaceModule.Eyes;

// LidClosure, not lid scale, so the all-zero default is a wide-awake audio-only face:
// no head pose, no social split, no forced eyelid movement.
public readonly record struct EyeContext(
    bool HeadValid,
    float HeadYawRate,
    float HeadPitchRate,
    bool HeadMoving,
    bool MotionOnset,
    bool Speaking,
    bool Hesitation,
    bool SocialGaze,
    float VorGain,
    float VorRecenterSeconds,
    float LidClosure,
    bool Asleep);
