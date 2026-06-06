namespace WKOpenVR.SyntheticFaceModule;

public sealed class SyntheticMouthProcessor
{
    private const float NoiseGate = 0.015f;
    private const float Gain = 5.5f;
    private const float Attack = 0.45f;
    private const float Release = 0.12f;

    private float envelope;

    public MouthShapeWeights Process(AudioFeatureFrame frame)
    {
        float target = Math.Clamp((frame.Rms - NoiseGate) * Gain, 0.0f, 1.0f);
        float coefficient = target > envelope ? Attack : Release;
        envelope += (target - envelope) * coefficient;

        float frontness = Math.Clamp((frame.ZeroCrossingRate - 0.06f) / 0.16f, 0.0f, 1.0f);
        float rounded = 1.0f - frontness;

        float jawOpen = envelope;
        float mouthClosed = Math.Clamp(1.0f - envelope * 1.8f, 0.0f, 1.0f);
        float funnel = envelope * rounded * 0.55f;
        float pucker = envelope * rounded * 0.35f;
        float stretch = envelope * frontness * 0.5f;
        float lowerRaiser = envelope * 0.2f;

        return new MouthShapeWeights(jawOpen, mouthClosed, funnel, pucker, stretch, lowerRaiser);
    }

    public void Reset()
    {
        envelope = 0;
    }
}
