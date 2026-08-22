using System.Numerics;

namespace WKOpenVR.SyntheticFaceModule.Head;

// Rates come from differencing successive rotations: several HMD drivers report a zero
// vecAngularVelocity, so the reported value cannot be relied on.
public sealed class HeadMotionTracker
{
    private const float NeutralTauSeconds = 30f;
    private const float EnergyTauSeconds = 2f;
    private const float MaxPlausibleRate = 30f;

    private bool _hasPrevious;
    private Quaternion _previousRotation = Quaternion.Identity;
    private float _timeSincePrevious;
    private long _previousSampleIndex;
    private bool _wasMoving;
    private bool _hasNeutral;

    public bool Valid { get; private set; }

    // rad/s about the head's own up axis; positive turns the face left.
    public float YawRate { get; private set; }

    // rad/s about the head's own right axis; positive tips the face up.
    public float PitchRate { get; private set; }

    // rad/s.
    public float Speed { get; private set; }

    // rad^2/s^2, averaged over a couple of seconds.
    public float MeanSquareSpeed { get; private set; }

    public bool Moving { get; private set; }

    public bool MotionOnset { get; private set; }

    // radians; positive looks up.
    public float Pitch { get; private set; }

    // Slowly learned, so a habitually low head does not read as dozing.
    public float NeutralPitch { get; private set; }

    // radians below the resting posture.
    public float PitchBelowNeutral { get; private set; }

    public void Update(in HeadInput head, float dtSeconds, float movingThreshold, float neutralFreezeBelow)
    {
        MotionOnset = false;

        if (!head.Valid)
        {
            Reset();
            return;
        }

        Valid = true;
        _timeSincePrevious += MathF.Max(0f, dtSeconds);

        Vector3 forward = Vector3.Transform(new Vector3(0f, 0f, -1f), head.Rotation);
        Pitch = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));

        if (!_hasNeutral)
        {
            NeutralPitch = Pitch;
            _hasNeutral = true;
        }

        bool newSample = !_hasPrevious || head.SampleIndex != _previousSampleIndex;
        if (newSample)
        {
            if (_hasPrevious && _timeSincePrevious > 1e-4f)
            {
                Vector3 rate = LocalRate(_previousRotation, head.Rotation, _timeSincePrevious);
                if (rate.Length() > MaxPlausibleRate)
                {
                    rate = Vector3.Zero;
                }

                PitchRate = rate.X;
                YawRate = rate.Y;
                Speed = rate.Length();
            }

            _previousRotation = head.Rotation;
            _previousSampleIndex = head.SampleIndex;
            _timeSincePrevious = 0f;
            _hasPrevious = true;
        }
        else if (_timeSincePrevious > 0.25f)
        {
            // The pose stopped updating: treat the head as still rather than holding a stale rate.
            PitchRate = 0f;
            YawRate = 0f;
            Speed = 0f;
        }

        float energyAlpha = Alpha(dtSeconds, EnergyTauSeconds);
        MeanSquareSpeed += ((Speed * Speed) - MeanSquareSpeed) * energyAlpha;

        Moving = Speed > movingThreshold;
        MotionOnset = Moving && !_wasMoving;
        _wasMoving = Moving;

        PitchBelowNeutral = NeutralPitch - Pitch;
        if (!Moving && PitchBelowNeutral < neutralFreezeBelow)
        {
            NeutralPitch += (Pitch - NeutralPitch) * Alpha(dtSeconds, NeutralTauSeconds);
            PitchBelowNeutral = NeutralPitch - Pitch;
        }
    }

    private void Reset()
    {
        Valid = false;
        YawRate = 0f;
        PitchRate = 0f;
        Speed = 0f;
        MeanSquareSpeed = 0f;
        Moving = false;
        Pitch = 0f;
        PitchBelowNeutral = 0f;
        _hasPrevious = false;
        _hasNeutral = false;
        _wasMoving = false;
        _timeSincePrevious = 0f;
    }

    // Rotation from the previous sample to this one, expressed in the head's own axes.
    private static Vector3 LocalRate(Quaternion previous, Quaternion current, float elapsedSeconds)
    {
        Quaternion delta = Quaternion.Normalize(Quaternion.Concatenate(Quaternion.Inverse(previous), current));
        if (delta.W < 0f)
        {
            delta = new Quaternion(-delta.X, -delta.Y, -delta.Z, -delta.W);
        }

        var axis = new Vector3(delta.X, delta.Y, delta.Z);
        float sinHalf = axis.Length();
        if (sinHalf < 1e-7f)
        {
            return Vector3.Zero;
        }

        float angle = 2f * MathF.Atan2(sinHalf, Math.Clamp(delta.W, -1f, 1f));
        return axis * (angle / (sinHalf * elapsedSeconds));
    }

    private static float Alpha(float dtSeconds, float tauSeconds)
    {
        if (dtSeconds <= 0f)
        {
            return 0f;
        }

        return 1f - MathF.Exp(-dtSeconds / tauSeconds);
    }
}
