namespace WKOpenVR.SyntheticFaceModule;

public interface IAudioFeatureSource : IDisposable
{
    void Start();

    bool TryRead(out AudioFeatureFrame frame);
}
