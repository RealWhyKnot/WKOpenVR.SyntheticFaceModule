using System.Numerics;

namespace WKOpenVR.SyntheticFaceModule.Head;

public readonly record struct HeadInput(
    bool Valid,
    Quaternion Rotation,
    Vector3 AngularVelocity,
    long SampleIndex,
    float AgeSeconds)
{
    public static readonly HeadInput None = new(false, Quaternion.Identity, Vector3.Zero, 0, 0f);
}
