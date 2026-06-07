using System.Diagnostics.CodeAnalysis;

namespace WKOpenVR.SyntheticFaceModule.Audio;

/// <summary>
/// Produces <see cref="AudioAnalysisFrame"/> snapshots from an audio input. Capture/analysis runs on
/// its own thread; <see cref="TryRead"/> returns the latest published snapshot without blocking the
/// per-frame update loop.
/// </summary>
public interface IAudioAnalysisSource : IDisposable
{
    void Start();

    bool TryRead([NotNullWhen(true)] out AudioAnalysisFrame? frame);
}
