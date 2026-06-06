namespace WKOpenVR.SyntheticFaceModule;

public readonly record struct AudioFeatureFrame(
    float Rms,
    float ZeroCrossingRate,
    int SampleRate);
