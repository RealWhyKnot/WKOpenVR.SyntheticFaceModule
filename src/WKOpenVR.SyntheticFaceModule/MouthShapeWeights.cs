namespace WKOpenVR.SyntheticFaceModule;

public readonly record struct MouthShapeWeights(
    float JawOpen,
    float MouthClosed,
    float LipFunnel,
    float LipPucker,
    float MouthStretch,
    float MouthRaiserLower);
